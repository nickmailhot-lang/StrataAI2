"""Private disposable-fixture hashes; never print credentials or verified hashes.

Encoding follows the .NET 10 PasswordHasher source:
https://github.com/dotnet/aspnetcore/blob/v10.0.0/src/Identity/Extensions.Core/src/PasswordHasher.cs
The old encoding is fixture input only, never a production hashing policy.
"""
import base64
import hashlib
import hmac
import json
import secrets
import struct
import sys
from pathlib import Path


def reject():
    raise SystemExit('Identity password-hash fixture validation failed.')


def main():
    if len(sys.argv) not in (3, 4) or sys.argv[1] not in ('legacy', 'verify-current'):
        reject()
    credentials = json.loads(Path(sys.argv[2]).read_text(encoding='utf-8'))
    password = credentials.get('password')
    if not isinstance(password, str) or not 12 <= len(password) <= 128:
        reject()
    if sys.argv[1] == 'legacy':
        if len(sys.argv) != 3:
            reject()
        salt = secrets.token_bytes(16)
        subkey = hashlib.pbkdf2_hmac('sha1', password.encode('utf-8'), salt, 1000, 32)
        # Captured privately by the owning CI script, not a console diagnostic.
        print(base64.b64encode(b'\x00' + salt + subkey).decode('ascii'))
        return
    if len(sys.argv) != 4:
        reject()
    encoded = Path(sys.argv[3]).read_text(encoding='utf-8').strip()
    try:
        payload = base64.b64decode(encoded, validate=True)
    except ValueError:
        reject()
    if len(payload) != 61 or payload[0] != 1:
        reject()
    prf, iterations, salt_length = struct.unpack('>III', payload[1:13])
    if prf != 2 or not 100_000 <= iterations <= 1_000_000 or salt_length != 16:
        reject()
    subkey = hashlib.pbkdf2_hmac('sha512', password.encode('utf-8'), payload[13:29], iterations, 32)
    if not hmac.compare_digest(subkey, payload[29:]):
        reject()


if __name__ == '__main__':
    main()
