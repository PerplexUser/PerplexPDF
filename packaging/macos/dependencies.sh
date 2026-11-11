# .NET release metadata https://dotnetcli.blob.core.windows.net/dotnet/release-metadata/10.0/releases.json
# PDFium wheel files and SHA-256 values https://pypi.org/project/pypdfium2/5.14.0/#files
runtime_version=10.0.14
case "$architecture" in
    arm64)
        rid=osx-arm64
        runtime_sha=19c5781dc2f87600865886b8774a4c5851caf1f5cf3f591185b922b0e51dea6b8c0ed0bf80ae4d3be2e083db662124258743e779fab6885322d641eff52b0382
        host_sha=2e78b66cfdb5e8813640687be3333c2dd62ede680eaf99f6935f6c10febb0120d428a8745120c44f8bfba39ce6260b5c1aec7108b76b74c158c5c0a303e64edb
        pdfium_url=https://files.pythonhosted.org/packages/d1/ea/14673bc9d8b7beeaa1eb46e9951b22543edaf2a4676c586e3b1e032ff6ee/pypdfium2-5.14.0-py3-none-macosx_13_0_arm64.whl
        pdfium_sha=2de384df66ba55fcaab0775f30f28ec1090af3dfa60276a07821efc96d993118
        ;;
    x86_64)
        rid=osx-x64
        runtime_sha=b13d453e60f3d718743ada5710d0174524cbf16dead74aa62af6cc258bc5f7310e8ec991fa9365f84bf78bb198417ddceb1381acf557ed52ab0300a21b671596
        host_sha=ff066a13974548a738094d399891747d73ff96ce18329e273814ec327b8195536ae2eb9bbf51ba413551d262c2b0c6f68cdc2141614cd9dde37263859493ae0c
        pdfium_url=https://files.pythonhosted.org/packages/a6/11/b720097b01fa0874854f2f6669cbea4e4ea4e075769687714fac64d68964/pypdfium2-5.14.0-py3-none-macosx_13_0_x86_64.whl
        pdfium_sha=e4e203ea9710fd00e5448edb6f1615dc8587035357f75f40b432dde0c33e8da1
        ;;
    *) return 1 ;;
esac
runtime_base=https://builds.dotnet.microsoft.com/dotnet/Runtime/$runtime_version
runtime_url=$runtime_base/dotnet-runtime-$runtime_version-$rid.tar.gz
host_url=$runtime_base/dotnet-apphost-pack-$runtime_version-$rid.tar.gz
