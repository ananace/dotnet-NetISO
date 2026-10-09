.NET NetISO Server Daemon
=========================

A .NET implementation of the server-side component for NetISO for the Xbox 360.

Features
--------

- Full support for regular .iso files
- Full support for ZArchive-compressed .ziso files
- Partial support for GOD games, both extracted as well as stored in .zip files

To-do
-----

- Support single-file GOD games, i.e. ones without a `[...].data` folder, commonly seen in XBLA titles
- Improve display names for ISO games
- Support more GOD archive kinds
- Handle non-recursive GOD directory scan

Usage
-----

```
NetISO serve --dir <dir>
```

Serve all games discovered in the given directory over NetISO, use `--recurse` for scanning recursively

```
NetISO info <game>
```

Display information about a game

```
NetISO convert <file> <destination>
```

Convert a game between one format and another, currently only supports .iso destination
