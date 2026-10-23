---
issue: 19
kind: added
breaking: false
manual: ["Back up the database and the media volume before every upgrade: that backup is the only way back.", "Install from the docker-compose.yml attached to a release, which names an exact version; upgrade by changing that version."]
---
QuickBuilder has release numbers, shown to Administrators in Settings and in the first log line. On start it refuses a database from a newer release, or one too old to upgrade directly, and says what to do. Old content keeps working when the content format changes.
