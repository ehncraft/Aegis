# Changelog

## [0.5.0](https://github.com/ehncraft/Aegis/compare/v0.4.0...v0.5.0) (2026-07-12)


### Features

* Aegis.Cli -- validate policies and evaluate decisions from the command line (closes [#14](https://github.com/ehncraft/Aegis/issues/14)) ([#73](https://github.com/ehncraft/Aegis/issues/73)) ([d0ad65c](https://github.com/ehncraft/Aegis/commit/d0ad65c90e56448586ceb5c34979a6f465c4b977))
* opt-in decision caching (closes [#11](https://github.com/ehncraft/Aegis/issues/11)) ([#75](https://github.com/ehncraft/Aegis/issues/75)) ([641a292](https://github.com/ehncraft/Aegis/commit/641a292cbc7e397f4a978124f71a1e9d73b91a73))

## [0.4.0](https://github.com/ehncraft/Aegis/compare/v0.3.0...v0.4.0) (2026-07-12)


### Features

* ASP.NET Core integration -- services.AddAegis(...) and HttpContext.User (closes [#7](https://github.com/ehncraft/Aegis/issues/7)) ([#70](https://github.com/ehncraft/Aegis/issues/70)) ([bb50ad7](https://github.com/ehncraft/Aegis/commit/bb50ad757b1680cac6c789b04f2d2ef1ef21c065))
* map ASP.NET Identity/IdentityServer/OpenIddict claims to AegisPrincipal (closes [#29](https://github.com/ehncraft/Aegis/issues/29), [#42](https://github.com/ehncraft/Aegis/issues/42), [#43](https://github.com/ehncraft/Aegis/issues/43), [#44](https://github.com/ehncraft/Aegis/issues/44), [#45](https://github.com/ehncraft/Aegis/issues/45)) ([#69](https://github.com/ehncraft/Aegis/issues/69)) ([cb54e03](https://github.com/ehncraft/Aegis/commit/cb54e033386b1dcd221cfffbab1bd91ab5adedaf))
* MSSQL-backed policy storage (closes [#28](https://github.com/ehncraft/Aegis/issues/28), [#38](https://github.com/ehncraft/Aegis/issues/38), [#39](https://github.com/ehncraft/Aegis/issues/39), [#40](https://github.com/ehncraft/Aegis/issues/40), [#41](https://github.com/ehncraft/Aegis/issues/41)) ([#67](https://github.com/ehncraft/Aegis/issues/67)) ([4a573e6](https://github.com/ehncraft/Aegis/commit/4a573e6f3fc4f72d4b302fcf5b193ce09a641ff0))
* validate policies at load time, not on first matching request (closes [#2](https://github.com/ehncraft/Aegis/issues/2)) ([#72](https://github.com/ehncraft/Aegis/issues/72)) ([3638975](https://github.com/ehncraft/Aegis/commit/3638975fe1ad3db5171fe83d6a81655df39ee592))


### Bug Fixes

* whitelist SQL Server identifiers per OWASP guidance ([#71](https://github.com/ehncraft/Aegis/issues/71)) ([bcb1c10](https://github.com/ehncraft/Aegis/commit/bcb1c10c66d4b46b8ea6f821acb09aaa48bbba98))

## [0.3.0](https://github.com/ehncraft/Aegis/compare/v0.2.4...v0.3.0) (2026-07-12)


### Features

* MSSQL-backed attribute provider (closes [#27](https://github.com/ehncraft/Aegis/issues/27), [#34](https://github.com/ehncraft/Aegis/issues/34), [#35](https://github.com/ehncraft/Aegis/issues/35), [#36](https://github.com/ehncraft/Aegis/issues/36), [#37](https://github.com/ehncraft/Aegis/issues/37)) ([#63](https://github.com/ehncraft/Aegis/issues/63)) ([d6b0116](https://github.com/ehncraft/Aegis/commit/d6b01160b97a1ac6d8b5c104635bff0029f38b8d))

## [0.2.4](https://github.com/ehncraft/Aegis/compare/v0.2.3...v0.2.4) (2026-07-12)


### Bug Fixes

* revert AegisCore to Ehncraft.Aegis.Core (nuget.org similarity block) ([#61](https://github.com/ehncraft/Aegis/issues/61)) ([a0a7784](https://github.com/ehncraft/Aegis/commit/a0a778470f08a2b7939377c6487bbfdf7bef9a54))

## [0.2.3](https://github.com/ehncraft/Aegis/compare/v0.2.2...v0.2.3) (2026-07-12)


### Bug Fixes

* use one-word PackageIds to sidestep the Aegis.* nuget.org block ([#58](https://github.com/ehncraft/Aegis/issues/58)) ([2b1476f](https://github.com/ehncraft/Aegis/commit/2b1476faddf5ecd0b89039ede45195d0c5ee2b0f))

## [0.2.2](https://github.com/ehncraft/Aegis/compare/v0.2.1...v0.2.2) (2026-07-12)


### Bug Fixes

* allow manually re-triggering publish for a partial release ([#56](https://github.com/ehncraft/Aegis/issues/56)) ([6ef955a](https://github.com/ehncraft/Aegis/commit/6ef955a91a323aa3386f1f3680ea1fbd2ad7569d))

## [0.2.1](https://github.com/ehncraft/Aegis/compare/v0.2.0...v0.2.1) (2026-07-11)


### Bug Fixes

* resolve NuGet package ID collision on Aegis.Core ([#54](https://github.com/ehncraft/Aegis/issues/54)) ([74aee60](https://github.com/ehncraft/Aegis/commit/74aee60249a1571f2816a0832e014f0b4a3d0a5d))

## [0.2.0](https://github.com/ehncraft/Aegis/compare/v0.1.0...v0.2.0) (2026-07-11)


### Features

* automated NuGet releases via release-please + Trusted Publishing ([#49](https://github.com/ehncraft/Aegis/issues/49)) ([517f42d](https://github.com/ehncraft/Aegis/commit/517f42dccabdfd9e47c7940e0a743e062d9a0d0a))
* initial Phase 1 scaffold ([9b0e3d7](https://github.com/ehncraft/Aegis/commit/9b0e3d712ad9c49fca22b51e2968952ada74acba))
