"""Pinned public-domain NASA photograph used only for CI quality regression."""
import hashlib
import pathlib
import urllib.request

url = 'https://raw.githubusercontent.com/scikit-image/scikit-image/v0.18.3/skimage/data/astronaut.png'
expected = '88431cd9653ccd539741b555fb0a46b61558b301d4110412b5bc28b5e3ea6cb5'
data = urllib.request.urlopen(url, timeout=45).read()
assert hashlib.sha256(data).hexdigest() == expected
output = pathlib.Path('artifacts/tests/contours')
output.mkdir(parents=True, exist_ok=True)
(output / 'source-photo.png').write_bytes(data)
print('Pinned NASA public-domain photo:', len(data), expected)
