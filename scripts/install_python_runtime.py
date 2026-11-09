"""Install pinned Wasmtime and CPython WASI artifacts for the local strategy worker."""

import hashlib
import platform
import shutil
import sys
import tarfile
import tempfile
import zipfile
from pathlib import Path
from urllib.request import urlopen

PYTHON_VERSION = "3.15.2"
WASMTIME_VERSION = "50.0.1"
PYTHON_SHA256 = "b8a63559c56760bbdc310dedb74fd69d10bcbafae26b705bea0ad353ad256888"
WASMTIME_SHA256 = {
    "aarch64-linux": "d737e077416d78fbfb0a008229ec27d6a0fd5a4924874b8c38b293c850cf4094",
    "aarch64-macos": "960e5216c7ebd7e9b45e6561d15821b8b801b7d54aee28c7b91921aeb30c65c8",
    "x86_64-linux": "685466455dd42a14774a86c36c8f2e45cbb9bd6d92a850139315df60a75498a5",
    "x86_64-macos": "ea1818fa8cbb51524847ab2ad22197e0051594fdc96cbd0cc2afb65832e11925",
}


def download(url, path, expected):
    with urlopen(url, timeout=60) as source, path.open("wb") as output:
        shutil.copyfileobj(source, output)
    with path.open("rb") as data:
        if hashlib.file_digest(data, "sha256").hexdigest() != expected:
            raise RuntimeError(f"Checksum mismatch for {url}")


def install(destination):
    system = {"Darwin": "macos", "Linux": "linux"}.get(platform.system())
    arch = {"arm64": "aarch64", "aarch64": "aarch64", "x86_64": "x86_64"}.get(platform.machine())
    target = f"{arch}-{system}"
    if target not in WASMTIME_SHA256:
        raise SystemExit("Python strategies require macOS or Linux on ARM64 or x86-64.")

    destination = destination.resolve()
    destination.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory(dir=destination.parent) as directory:
        stage = Path(directory)
        python_asset = f"python-{PYTHON_VERSION}-wasi_sdk-33.zip"
        wasmtime_asset = f"wasmtime-v{WASMTIME_VERSION}-{target}"
        download(f"https://github.com/brettcannon/cpython-wasi-build/releases/download/v{PYTHON_VERSION}/{python_asset}",
                 stage / "python.zip", PYTHON_SHA256)
        download(f"https://github.com/bytecodealliance/wasmtime/releases/download/v{WASMTIME_VERSION}/{wasmtime_asset}.tar.xz",
                 stage / "wasmtime.tar.xz", WASMTIME_SHA256[target])

        with tarfile.open(stage / "wasmtime.tar.xz") as package, (stage / "wasmtime").open("wb") as output:
            with package.extractfile(f"{wasmtime_asset}/wasmtime") as executable:
                shutil.copyfileobj(executable, output)
        (stage / "wasmtime").chmod(0o755)

        with zipfile.ZipFile(stage / "python.zip") as package:
            with package.open("python.wasm") as wasm, (stage / "python.wasm").open("wb") as output:
                shutil.copyfileobj(wasm, output)
            with zipfile.ZipFile(stage / "python315.zip", "w") as stdlib:
                prefix = "lib/python3.15/"
                for entry in package.infolist():
                    if entry.filename.startswith(prefix) and not entry.is_dir():
                        stdlib.writestr(entry.filename[len(prefix):], package.read(entry))

        destination.mkdir(exist_ok=True)
        for name in ("wasmtime", "python.wasm", "python315.zip"):
            (stage / name).replace(destination / name)
    print(destination)


if __name__ == "__main__":
    install(Path(sys.argv[1]) if len(sys.argv) > 1 else Path(".botarena/python"))
