"""Extract the pinned user-supplied NSIS payload for local Windows acceptance."""

from hashlib import sha256
import json
from lzma import FILTER_LZMA1, FORMAT_RAW, LZMADecompressor
from pathlib import Path
import shutil
from struct import unpack_from

REPO = Path(__file__).resolve().parents[1]
LOCK_PATH = Path(__file__).with_name("tesseract-local-test.lock.json")
NSIS_SIGNATURE = b"\xef\xbe\xad\xdeNullsoftInst"
NSIS_EXTRACT_FILE = 20
NSIS_ENTRY_BYTES = 28


def checked_bytes(path, expected, maximum):
    if path.stat().st_size > maximum:
        raise ValueError(f"File exceeds the expected size: {path.name}")
    data = path.read_bytes()
    if sha256(data).hexdigest() != expected:
        raise ValueError(f"Unexpected SHA-256 for {path.name}")
    return data


def unpack_pinned_installer(data, wanted):
    signature = data.find(NSIS_SIGNATURE, 4, 4 * 1024 * 1024)
    if signature < 4:
        raise ValueError("Expected an NSIS installer.")
    start = signature - 4
    _, _, _, _, _, header_size, archive_size = unpack_from("<7I", data, start)
    position = start + 28
    archive_end = start + archive_size
    properties, dictionary = unpack_from("<BI", data, position)
    if (not 0 < header_size < 8 * 1024 * 1024 or not position + 9 < archive_end <= len(data)
            or properties >= 225 or not 0 < dictionary <= 64 * 1024 * 1024):
        raise ValueError("Unexpected NSIS header.")
    decoder = LZMADecompressor(
        format=FORMAT_RAW,
        filters=[{"id": FILTER_LZMA1, "dict_size": dictionary, "lc": properties % 9,
                  "lp": (properties // 9) % 5, "pb": properties // 45}],
    )
    decoded = decoder.decompress(data[position + 5:archive_end - 4], max_length=128 * 1024 * 1024)
    if not decoder.eof or len(decoded) < header_size + 4 or unpack_from("<I", decoded)[0] != header_size:
        raise ValueError("Unexpected NSIS data size.")
    header = memoryview(decoded)[4:4 + header_size]
    payload = memoryview(decoded)[4 + header_size:]
    entries, count = unpack_from("<II", header, 20)
    strings, _ = unpack_from("<II", header, 28)
    strings_end, _ = unpack_from("<II", header, 36)
    if not 0 <= entries <= entries + count * NSIS_ENTRY_BYTES <= strings <= strings_end <= len(header):
        raise ValueError("Unexpected NSIS block table.")

    extracted = {}
    for index in range(count):
        opcode, _, name_index, offset, *_ = unpack_from("<7I", header, entries + index * NSIS_ENTRY_BYTES)
        if opcode != NSIS_EXTRACT_FILE:
            continue
        name_at = strings + name_index * 2
        if not strings <= name_at < strings_end:
            continue
        end = name_at
        while end + 2 <= strings_end and header[end:end + 2] != b"\0\0":
            end += 2
        name = header[name_at:end].tobytes().decode("utf-16-le", errors="replace")
        if name not in wanted:
            continue
        if offset + 4 > len(payload):
            raise ValueError(f"Invalid file offset for {name}")
        length = unpack_from("<I", payload, offset)[0]
        if length < 1 or offset + 4 + length > len(payload):
            raise ValueError(f"Invalid file size for {name}")
        content = payload[offset + 4:offset + 4 + length]
        if sha256(content).hexdigest() != wanted[name]:
            raise ValueError(f"Unexpected payload hash for {name}")
        extracted[name] = content
    missing = wanted.keys() - extracted.keys()
    if missing:
        raise ValueError(f"Incomplete Tesseract payload: {', '.join(sorted(missing))}")
    return extracted


def copy_local_ocr(inputs, staged):
    lock = json.loads(LOCK_PATH.read_text(encoding="utf-8"))
    installer = checked_bytes(inputs / lock["installer"], lock["installerSha256"], 64 * 1024 * 1024)
    payload = unpack_pinned_installer(installer, lock["payloadSha256"])

    program = staged / "tools/tesseract"
    tessdata = program / "tessdata"
    (tessdata / "configs").mkdir(parents=True)
    notices = staged / "licenses/tesseract-local-test"
    shutil.copytree(REPO / "licenses/tesseract-local-test", notices)
    destinations = {
        "AUTHORS": notices / "AUTHORS",
        "LICENSE": notices / "LICENSE",
        "pdf.ttf": tessdata / "pdf.ttf",
        "pdf": tessdata / "configs/pdf",
    }
    for name, content in payload.items():
        destination = destinations.get(name, program / name)
        with destination.open("xb") as stream:
            stream.write(content)
    for name, checksum in lock["languageSha256"].items():
        (tessdata / name).write_bytes(checked_bytes(inputs / name, checksum, 16 * 1024 * 1024))
    shutil.copyfile(LOCK_PATH, notices / LOCK_PATH.name)
