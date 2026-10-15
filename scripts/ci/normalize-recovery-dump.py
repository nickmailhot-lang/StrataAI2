"""Canonicalize only unordered COPY rows and reparsed CHECK/session syntax."""
import sys


def normalize(lines, output):
    rows = None
    table = False
    table_section = False
    for line in lines:
        if rows is not None:
            if line == "\\.\n":
                output.writelines(sorted(rows))
                output.write(line)
                rows = None
            else:
                rows.append(line)
        elif line.startswith("COPY ") and line.endswith(" FROM stdin;\n"):
            output.write(line)
            rows = []
        elif line.startswith(("\\restrict ", "\\unrestrict ")):
            continue
        elif line.startswith("-- Name: "):
            table_section = "; Type: TABLE;" in line
            output.write(line)
        elif table_section and line.startswith("CREATE TABLE "):
            table = True
            output.write(line)
        elif table and line.startswith("    CONSTRAINT ") and " CHECK (" in line:
            # Every CHECK is compared separately via pg_get_constraintdef(true).
            continue
        else:
            output.write(line)
            if table and line == ");\n":
                table = False
    if rows is not None or table:
        raise ValueError("incomplete_recovery_dump")


if __name__ == "__main__":
    normalize(sys.stdin, sys.stdout)
