# Third-party notices

This file lists third-party software that a Modulus release ships or depends on, the license that governs it, and how each was checked.
Anything marked **Open** is not approved for release. Verified facts are stated with their source.

## Profile-photo decoder: SkiaSharp 4.153.1 (accepted by the owner's decision, 2026-10-25; not a legal review)

Used by `Modulus.Identity` for profile-photo validation, decoding and re-encoding. The code now uses SkiaSharp. The owner chose it on
2026-10-25 over keeping ImageSharp 2.1.13. **Open:** the native bundle contains components whose licences the repository policy does not
allow (AGENTS.md: MIT, Apache-2.0 or BSD only).

**Owner decision (2026-10-25).** The owner chose to use SkiaSharp and accept the components listed below as a waiver of the repository
policy for this package. This records that decision. It is not a licence-compliance review: the libmicrohttpd (LGPL), giflib
(MPL/GPL/LGPL) and Adobe DNG SDK terms still need checking by counsel before a commercial release.

**Packages.** `SkiaSharp` 4.153.1 (managed wrapper) and `SkiaSharp.NativeAssets.Linux.NoDependencies` 4.153.1 (Linux native library).
Both declare `MIT` in their NuGet metadata (verified from the `.nuspec`).

**Native bundle (verified from `THIRD-PARTY-NOTICES.txt` in the `NoDependencies` package, 4.153.1).** The Linux `libSkiaSharp.so` carries:

| Component | Licence stated in the bundle | Policy |
| --- | --- | --- |
| Skia, ANGLE, HarfBuzz, libpng, libjpeg-turbo, libwebp, zlib, expat, freetype, ICU, jsoncpp, piex, sfntly, SPIR-V, etc1, imgui, sdl | BSD, MIT, Apache-2.0, zlib, Unicode | allowed |
| `gif` (giflib) | tri-licence: MPL 1.1, GPL 2.0 or LGPL 2.1 (the recipient may choose) | copyleft option present; MPL or LGPL choice needed |
| `libmicrohttpd` | LGPL 2.1, or eCos when built without HTTPS | LGPL: not allowed by policy |
| `DNG SDK` | Adobe Systems DNG SDK License Agreement | not an OSI licence; needs review |

These components are in the shipped native library whether or not the profile-photo code uses them. The `DNG` and `libmicrohttpd` parts
are the ones that fail the policy. This is the reason SkiaSharp was removed on 2026-10-24 (the bundle, not the wrapper), and the same
objection applies now.

**Runtimes.** Verified here: Linux x64 only (the tests ran on net8.0 and net10.0 on Linux x64). Windows, macOS, Linux ARM64, Linux ARM and
Linux musl are not tested, and the bundle ships separate native files for those platforms (`runtimes/*`).

**Behaviour that protects the upload.** The decoder runs only after: the size check (5 MB), the format check by magic bytes, the animation check,
the colour-profile check, and the header dimension check (4096 px per side). Malformed or truncated input is refused as undecodable. The stored
image is re-encoded from decoded pixels with EXIF, XMP and IPTC metadata removed.

## Earlier candidate, not used

SkiaSharp 2.88.6 was the first decoder. Its managed code is MIT, but its native bundle includes the Adobe DNG SDK agreement, libmicrohttpd
(LGPL or eCos) and gif (MPL/GPL/LGPL). It was removed for the reasons above.

PhotoSauce.MagicScaler 0.15.0 (MIT) was considered and not used. Its native component bundles libheif, which is under the LGPL 3.0, so it fails
the complete-bundle rule.
