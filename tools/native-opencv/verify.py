"""Validate the actual shipped ELF; never patch binaries to claim alignment."""
import pathlib
import struct
import sys
import zipfile


def verify(data, abi):
    assert data[:6] == b'\x7fELF\x02\x01'
    header = struct.unpack_from('<16sHHIQQQIHHHHHH', data)
    assert header[2] == {'arm64-v8a': 183, 'x86_64': 62}[abi]
    loads = 0
    for i in range(header[10]):
        kind, flags, offset, address, _, files, memory, alignment = struct.unpack_from('<IIQQQQQQ', data, header[5] + i * header[9])
        if kind == 1:
            loads += 1
            assert alignment >= 16384 and (offset - address) % alignment == 0, (abi, offset, address, alignment)
    assert loads >= 2
    print('Verified OpenCV ELF architecture and 16 KB LOAD alignment:', abi)


path = pathlib.Path(sys.argv[1])
if path.suffix == '.apk':
    with zipfile.ZipFile(path) as apk:
        for abi in ('arm64-v8a', 'x86_64'):
            verify(apk.read(f'lib/{abi}/libOpenCvSharpExtern.so'), abi)
else:
    verify(path.read_bytes(), sys.argv[2])
