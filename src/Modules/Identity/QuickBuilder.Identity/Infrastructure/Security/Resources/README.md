# Common-password list

`common-passwords-12plus.txt` backs the password policy's "not a commonly used or
breached password" rule (spec FR-008, research R-15). It ships inside the image so the
check works offline (constitution Principle II).

## Source

[SecLists](https://github.com/danielmiessler/SecLists), pinned to commit
`8420764ea28d5cdc1a8bbb8311736a2235be28c4`. Three files from
`Passwords/Common-Credentials/` are used:

| File | Entries |
|---|---|
| `xato-net-10-million-passwords-1000000.txt` | 1,000,000 |
| `Pwdb_top-1000000.txt` | 1,000,000 |
| `100k-most-used-passwords-NCSC.txt` | 99,840 |

## Filtering

Passwords shorter than 12 characters are already rejected by the length rule, so they
are dropped. Entries longer than 128 characters can't be submitted, so they are dropped
too. The rest are trimmed, lower-cased (ASCII) and de-duplicated. The policy compares
case-insensitively, so lower-casing loses nothing.

Result: 73,078 entries, about 1 MB.

## Regenerating

Run this from an empty directory with `curl`, `awk`, `sed`, `tr` and `sort`. Change the
commit deliberately, and update this file in the same change.

```bash
sha=8420764ea28d5cdc1a8bbb8311736a2235be28c4
base="https://raw.githubusercontent.com/danielmiessler/SecLists/$sha/Passwords/Common-Credentials"
for f in xato-net-10-million-passwords-1000000.txt Pwdb_top-1000000.txt 100k-most-used-passwords-NCSC.txt; do
  curl -fsSL -o "$f" "$base/$f"
done
cat xato-net-10-million-passwords-1000000.txt Pwdb_top-1000000.txt 100k-most-used-passwords-NCSC.txt \
  | tr -d '\r' \
  | sed -E 's/^[[:space:]]+//; s/[[:space:]]+$//' \
  | awk 'length($0) >= 12 && length($0) <= 128' \
  | LC_ALL=C tr '[:upper:]' '[:lower:]' \
  | LC_ALL=C sort -u > common-passwords-12plus.txt
```

## Licence

SecLists is distributed under the MIT License:

```text
MIT License

Copyright (c) 2018 Daniel Miessler

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```
