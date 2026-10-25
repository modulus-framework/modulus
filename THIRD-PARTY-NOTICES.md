# Third-party notices

This file lists third-party software that a Modulus release ships or depends on, the license that governs it, and how each was checked.
Anything marked **Open** is not approved for release. Verified facts are stated with their source.

## Profile-photo decoder: SixLabors.ImageSharp 2.1.13

Used by `Modulus.Identity` for profile-photo validation, decoding and re-encoding. Replaces SkiaSharp, which was removed because its native
bundle includes the Adobe DNG SDK agreement and copyleft options (libmicrohttpd LGPL, gif MPL/GPL/LGPL).

**Why 2.1.13 and not 3.x or 4.x.** Versions 3.0 and later are under the Six Labors Split License, which requires a commercial licence for
companies above a revenue threshold. That is an additional licensing obligation the repository policy does not allow. The 2.x line is
Apache-2.0.

**Licence (verified).** The `LICENSE` file at the package's source commit `2c3967b22543545c902fb1fecf57d44025bda84d`
(`SixLabors/ImageSharp`, fetched from the upstream repository) is the Apache License, Version 2.0. The package metadata also declares
`Apache-2.0`. The upstream repository ships no separate notices file.

**Bundle (verified).** The `SixLabors.ImageSharp` 2.1.13 package is managed code only. It contains no native binaries, so no
platform-specific native runtime is involved.

**Managed dependencies (verified from their package metadata).** `System.Buffers` 4.5.1, `System.Memory` 4.5.4 and
`System.Numerics.Vectors` 4.5.0 (licence URL: dotnet/corefx `LICENSE.TXT`, MIT); `System.Runtime.CompilerServices.Unsafe` 5.0.0 and
`System.Text.Encoding.CodePages` 5.0.0 (MIT). All are MIT.

**Advisories (verified against the GitHub advisory database).** NuGet's audit reports advisories for ImageSharp 2.1.13. The fixes for the
ones below exist only in 4.1.2 and later, which is under the Split License, so they cannot be upgraded to within the policy. Each one was
checked against the code path used by the profile-photo feature. Five are suppressed in `Directory.Build.props` (`NuGetAuditSuppress`):

| Advisory | Severity | Affected code | Reachable from profile photos? |
| --- | --- | --- | --- |
| GHSA-gwg2-r3hj-4w44 (ICC CLUT parsing) | medium | colour-profile parser | No: an upload with an embedded colour profile (JPEG APP2 `ICC_PROFILE`, PNG `iCCP`, WebP ICC flag or chunk) is refused by the structure walk before any decoder runs. |
| GHSA-wmxv-xphr-5c9g (BigTIFF decoder) | medium | TIFF decoder | No: TIFF is refused by the format gate, which accepts only JPEG, PNG and WebP by magic bytes. |
| GHSA-jjfr-hcj7-qf5w (TIFF CCITT T6 encoder) | high | TIFF encoder | No: the encoder is never called, and TIFF is refused by the format gate. |
| GHSA-j9gm-c75j-xc9q (TIFF CCITT T4 encoder) | high | TIFF encoder | No: as above. |
| GHSA-j3p4-wp97-rph4 (HistogramEqualization) | high | one processing operation | No: never called. |

The other advisories listed for ImageSharp are fixed in 2.1.13 (GIF, older out-of-bounds and data-leak issues), or affect only 3.x and 4.x
(EXR, LUT16 and the 4.x ICC paths).

**Accepted risk: decided 2026-10-24 on the owner's behalf** (the owner asked for the open items to be decided). The five suppressions are
accepted as a risk, not as a formal sign-off. The owner can reverse this, choose another decoder, or fix the TIFF and ICC paths upstream.
Every generated app that has sign-in carries the same five suppressions in its `Directory.Build.props`. The release stays on hold until the
other evidence in `docs/PREMODULES_PLAN.md` is in.

**Runtimes.** The package is pure managed code, so the same code runs on any platform .NET supports. **Verified here: Linux x64 only.** Windows,
macOS, Linux ARM64, Linux ARM and Linux musl are **not yet tested**, and no claim of decoding on them is made.

**Behaviour that protects the upload.** The decoder runs only after: the size check (5 MB), the format check by magic bytes, the animation check,
the colour-profile check, and the header dimension check (4096 px per side). Malformed or truncated input is refused as undecodable. The stored
image is re-encoded from decoded pixels with EXIF, XMP and IPTC metadata removed.

## Earlier candidate, not used

SkiaSharp 2.88.6 was the first decoder. Its managed code is MIT, but its native bundle includes the Adobe DNG SDK agreement, libmicrohttpd
(LGPL or eCos) and gif (MPL/GPL/LGPL). It was removed for the reasons above.

PhotoSauce.MagicScaler 0.15.0 (MIT) was considered and not used. Its native component bundles libheif, which is under the LGPL 3.0, so it fails
the complete-bundle rule.
