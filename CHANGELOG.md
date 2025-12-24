# Changelog

All notable changes to the `SessionLevelsStrategy` project will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.0.0/),
and this project adheres to [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
