# Third-party notices

QuickBuilder is MIT-licensed (see `LICENSE`). The Docker image and published builds also
contain the third-party software below, redistributed under the terms stated. Packages
used only to build or test the product are not listed here.

## Native image processing (libvips)

The Media module resizes images with [NetVips](https://github.com/kleisauke/net-vips)
(MIT License) over **libvips 8.18.7**, shipped as the `NetVips.Native.*` packages.
The native libraries are included **unmodified**, as separate shared objects that the
application loads dynamically at run time (`runtimes/<rid>/native/`). You may replace them
with your own build of the same libraries.

Corresponding source for the LGPL-licensed libraries is available from their upstream
projects at the versions below, and from the packaging projects that build them:
<https://github.com/kleisauke/libvips-packaging> and
<https://github.com/lovell/sharp-libvips>.

| Library       | Version | Used under the terms of |
|---------------|---------|-------------------------|
| aom           | 3.15.1  | BSD 2-Clause + [Alliance for Open Media Patent License 1.0](https://aomedia.org/license/patent-license/) |
| cairo         | 1.18.6  | Mozilla Public License 1.1 |
| cgif          | 0.5.4   | MIT License |
| expat         | 2.8.5   | MIT License |
| fontconfig    | 2.18.3  | [fontconfig License](https://gitlab.freedesktop.org/fontconfig/fontconfig/blob/main/COPYING) (BSD-like) |
| freetype      | 2.14.3  | [freetype License](https://git.savannah.gnu.org/cgit/freetype/freetype2.git/tree/docs/FTL.TXT) (BSD-like) |
| fribidi       | 1.0.17  | LGPLv3 |
| glib          | 2.90.0  | LGPLv3 |
| harfbuzz      | 14.5.0  | MIT License |
| highway       | 1.4.0   | BSD 3-Clause |
| lcms          | 2.19.1  | MIT License |
| libarchive    | 3.8.9   | BSD 2-Clause |
| libexif       | 0.6.26  | LGPLv3 |
| libffi        | 3.8.0   | MIT License |
| libheif       | 1.23.5  | LGPLv3 |
| libimagequant | 2.4.1   | [BSD 2-Clause](https://github.com/lovell/libimagequant/blob/main/COPYRIGHT) |
| libnsgif      | —       | MIT License |
| libpng        | 1.6.58  | [libpng License](https://github.com/pnggroup/libpng/blob/master/LICENSE) |
| librsvg       | 2.63.2  | LGPLv3 |
| libtiff       | 4.7.2   | [libtiff License](https://gitlab.com/libtiff/libtiff/blob/master/LICENSE.md) (BSD-like) |
| libultrahdr   | 2.0.2   | MIT License |
| libvips       | 8.18.7  | LGPLv3 |
| libwebp       | 1.6.0   | New BSD License |
| libxml2       | 2.15.4  | MIT License |
| mozjpeg       | 0826579 | [zlib License, IJG License, BSD 3-Clause](https://github.com/mozilla/mozjpeg/blob/master/LICENSE.md) |
| pango         | 1.58.2  | LGPLv3 |
| pixman        | 0.46.4  | MIT License |
| proxy-libintl | 0.5     | LGPLv3 |
| zlib-ng       | 2.3.3   | [zlib License](https://github.com/zlib-ng/zlib-ng/blob/develop/LICENSE.md) |

Use of libraries under the terms of the LGPLv3 is via the "any later version" clause of
the LGPLv2 or LGPLv2.1. Several of these libraries (librsvg, pango, cairo, libtiff) are
compiled into libvips but are never used by QuickBuilder: the Media module blocks every
image loader except JPEG, PNG, GIF, WebP and HEIF/AVIF (research MV-09).

When updating `NetVips.Native.*` in `Directory.Packages.props`, regenerate this table from
the package's own `THIRD-PARTY-NOTICES.md` and `versions.json`.

## Storage providers

| Package | Version | Licence |
|---------|---------|---------|
| AWSSDK.S3 and AWSSDK.Core | 4.0.103.4 | [Apache License 2.0](https://github.com/aws/aws-sdk-net/blob/main/License.txt) |
| Azure.Storage.Blobs, Azure.Storage.Common, Azure.Core | 12.29.2 | [MIT License](https://github.com/Azure/azure-sdk-for-net/blob/main/LICENSE.txt) |
| Azure.Identity | 1.21.0 | [MIT License](https://github.com/Azure/azure-sdk-for-net/blob/main/LICENSE.txt) |

These are loaded only when an operator selects `Storage:Provider = S3` or `AzureBlob`.

## Email

| Package | Version | Licence |
|---------|---------|---------|
| MailKit | 4.17.0 | [MIT License](https://github.com/jstedfast/MailKit/blob/master/LICENSE) |
| MimeKit | 4.17.0 | [MIT License](https://github.com/jstedfast/MimeKit/blob/master/LICENSE) |
| BouncyCastle.Cryptography | 2.6.2 | [MIT License](https://github.com/bcgit/bc-csharp/blob/master/LICENSE.html) |

These send form notifications through the operator's SMTP server (018). They make no
connection unless `Email:Smtp:Host` is set. BouncyCastle comes with MimeKit for S/MIME,
which QuickBuilder does not use.

## Published operator documentation

The operator documentation site published for each release (built from `docs/operators/`,
never part of the Docker image) is generated with [VitePress](https://github.com/vuejs/vitepress)
1.6.4, and its pages include the VitePress default theme's scripts and styles, together with
[Vue](https://github.com/vuejs/core) and the [MiniSearch](https://github.com/lucaong/minisearch)
local search index. All are used under the MIT License.
