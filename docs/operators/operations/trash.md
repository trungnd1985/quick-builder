# Trash

Deleting a page, shared component, collection item or media file moves it to its site's
trash, where members can restore it until it is purged. A background job deletes trashed
content permanently after [`Trash:RetentionDays`](../reference/configuration.md#trash-retentiondays);
a change to that setting applies at once to everything already in the trash.

- **Who can do what**: Designers and Editors can delete to the trash and restore what they
  are allowed to delete. Only Administrators can delete permanently or empty the trash.
- **Storage**: media files in the trash keep using storage until they are purged. To free
  the space sooner, an Administrator can delete them permanently or empty the trash.
- **Collections** do not go to the trash: deleting a collection deletes it and its items
  permanently.
- **Backups**: the trash is part of the database, so a full backup includes it, and a
  restore brings back whatever was in the trash at that moment.
