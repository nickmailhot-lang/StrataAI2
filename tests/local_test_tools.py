"""Resolve the installed shell before refusal fixtures replace PATH."""
import os
import shutil


def test_bash():
    if os.name == 'nt':
        return 'C:/Program Files/Git/bin/bash.exe'
    executable = shutil.which('bash')
    if not executable:
        raise RuntimeError('Bash is required for shell contracts')
    return os.path.realpath(executable)
