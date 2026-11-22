# USB ID database

`usb.ids` is an unmodified snapshot of the [USB ID Repository](http://www.linux-usb.org/usb-ids.html), downloaded from its [SourceForge mirror](https://linux-usb.sourceforge.net/usb.ids).

- Upstream version: 2026.06.26 (the version served on 2026-11-22)
- SHA-256: `f5a48b0cc8dae1607c2f0bae6b8dc13f2ecef69dbeaeaf34b4be8e280d34dba4`
- License: BSD-3-Clause, selected from upstream's dual-license options. See [third-party notices](../THIRD-PARTY-NOTICES.txt).

The snapshot is embedded in `keypivo.core.dll`. Name lookups work offline and never update the database at runtime. Product names are matched by both VID and PID; Windows' device description remains a separate field.

To update the snapshot, download `usb.ids` from the source above, retain its header, update the version and SHA-256 here, and run the application tests. Commit the data update for review alongside these metadata changes.
