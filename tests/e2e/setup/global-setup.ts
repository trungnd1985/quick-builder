// What every run needs once, before any installation starts (specs/025-e2e-harness, E2E-03):
// Docker, the image under test, and a valid accessibility exceptions file. Installations
// themselves are started by the setup projects, so only the ones selected tests need start.

import { rm } from 'node:fs/promises'
import path from 'node:path'
import { readExceptions } from '../fixtures/exceptions.js'
import { REPOSITORY_ROOT, RESULTS, run } from './installations.js'

const NO_DOCKER = 'Docker is not available: start Docker and run again.'

export default async function globalSetup(): Promise<void> {
  // FR-020: locally, say so and stop before reporting a single test; in CI, fail the job.
  if ((await run('docker', ['info', '--format', '{{.ServerVersion}}'])).code !== 0) {
    if (process.env.QB_E2E_REQUIRE_DOCKER === '1') {
      throw new Error(`${NO_DOCKER} (QB_E2E_REQUIRE_DOCKER is set.)`)
    }
    console.error(NO_DOCKER)
    process.exit(1)
  }

  // The image under test: named by the caller (CI, release), or built from this checkout.
  if (!process.env.QB_E2E_IMAGE) {
    const image = 'quickbuilder:e2e'
    console.log(`Building ${image} from ${REPOSITORY_ROOT} (set QB_E2E_IMAGE to use an existing image)…`)
    const build = await run('docker', ['build', '-t', image, '.'])
    if (build.code !== 0) {
      throw new Error(`docker build failed:\n${build.output}`)
    }
    // Setup projects run in worker processes, which inherit this environment.
    process.env.QB_E2E_IMAGE = image
  }
  console.log(`Image under test: ${process.env.QB_E2E_IMAGE}`)

  // contracts/a11y-exceptions.md rules 1 to 3: a malformed list fails before any test runs.
  await readExceptions()
  await rm(path.join(RESULTS, 'a11y-matched'), { recursive: true, force: true })
}
