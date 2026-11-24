// Where the admin app is served, learned at run time rather than fixed at build time (#51).
//
// The host serves the admin at `{Hosting:BasePath}/admin/`, and the base path is an operator
// setting, so a build cannot know it. A production build uses relative asset addresses
// (vite.config.ts) and every chunk sits in `assets/`, so this module's own address gives the
// admin's: `.../admin/assets/x.js` -> `.../admin/`. The dev server serves at its fixed base.

/** The admin app's root, with a trailing slash: `/admin/`, or `/cms/admin/` under a base path. */
export const ADMIN_BASE: string = import.meta.env.DEV
  ? import.meta.env.BASE_URL
  : new URL('../', import.meta.url).pathname

/** The installation's root, with a trailing slash: `/`, or `/cms/` under a base path. */
export const APP_ROOT: string = ADMIN_BASE.replace(/admin\/$/, '')
