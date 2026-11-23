Every site serves its media files at `/_media/{id}.{ext}` on its own host names. The
`/_media` path is reserved on every site host: if a reverse proxy handled that path itself,
it must stop.
