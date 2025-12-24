# Changelog

All notable changes to the `SessionLevelsStrategy` project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.5.5] - 2025-12-23
### Changed
- **Persistence Disabled:** Disabled `LoadLevels` and `SaveLevels`. The strategy now relies entirely on loaded chart history to generate levels, ensuring perfect synchronization and resolving visual artifacts in playback.

## [1.5.4] - 2025-12-23
### Fixed
- **Duplicate Levels:** Implemented fuzzy matching to merge restored levels with newly calculated ones, preventing "double lines" when timestamps differ slightly.

## [1.5.3] - 2025-12-23
### Changed
- **Gap Handling:** Stale levels (older than 12h relative to chart start) are now filtered out preventing "weird lines".
### Added
- **Missing History Warning:** Red alert on chart when levels are hidden due to missing history, prompting user to load more days.

## [1.5.2] - 2025-12-23
### Fixed
- **Validation Error:** Fixed "Quantity is 0" error by setting default `Quantity = 1`.

## [1.5.1] - 2025-12-23
### Added
- **Local Screenshots:** Added `EnableLocalScreenshots` to allow saving chart images without enabling email alerts.
### Changed
- **Strategy Version:** Updated to v1.5.1.

## [1.5.0] - 2025-12-23
### Added
- **Dynamic TP Updates:** Target orders (TP1/TP2) now automatically adjust their price to follow the Global VWAP and Opposite Session Levels if they move while the order is working.
- **Version Tracking:** Added `CHANGELOG.md` and explicit version display on the chart panel.

### Changed
- Refactored `ManagePositionExit` to support dynamic price updates for active orders.

## [1.4.0] - 2025-12-23
### Added
- **Multi-Contract Support:** Logic to split position into TP1 (Closer) and TP2 (Farther).
- **Smart Protection:** Stop Loss moves to Breakeven when TP1 is filled.

### Fixed
- Fixed orphan order issues where stops were not correctly associated with the remaining position quantity.
