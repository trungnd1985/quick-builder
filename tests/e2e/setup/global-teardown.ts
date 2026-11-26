// FR-023b: an accessibility exception that no longer occurs fails the run, so the list only
// shrinks. Only when QB_E2E_CHECK_EXCEPTIONS=1 (both CI jobs set it): the suite cannot tell a
// full run from a filtered one, and a filtered run does not visit every screen (E2E-09).

import { readdir, readFile } from 'node:fs/promises'
import path from 'node:path'
import { exceptionKey, readExceptions } from '../fixtures/exceptions.js'
import { RESULTS } from './installations.js'

export default async function globalTeardown(): Promise<void> {
  if (process.env.QB_E2E_CHECK_EXCEPTIONS !== '1') {
    return
  }

  const directory = path.join(RESULTS, 'a11y-matched')
  const matched = new Set<string>()
  const files = await readdir(directory).catch(() => [] as string[])
  for (const file of files) {
    // One JSON string per line, appended by each worker's scans (fixtures/a11y.ts).
    for (const line of (await readFile(path.join(directory, file), 'utf8')).split('\n')) {
      if (line.trim()) matched.add(JSON.parse(line) as string)
    }
  }

  const unused = (await readExceptions()).filter((e) => !matched.has(exceptionKey(e)))
  if (unused.length > 0) {
    throw new Error(
      'These accessibility exceptions no longer occur; remove them from a11y-exceptions.json:\n  ' +
        unused.map((e) => `${e.rule} on ${e.screen}`).join('\n  '),
    )
  }
}
