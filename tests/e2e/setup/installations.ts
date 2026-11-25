// Throwaway installations of the image under test (specs/025-e2e-harness, E2E-03, E2E-04).
//
// Each kind is its own compose project on its own loopback port, started from the reference
// compose file, so `down -v` here can never touch an operator's own deployment of this
// checkout (project `quick-builder`) or the smoke tests' (`quickbuilder-smoke`, port 18080).

import { execFile } from 'node:child_process'
import { mkdir, writeFile } from 'node:fs/promises'
import path from 'node:path'

export type Kind = 'seeded' | 'fresh' | 'subpath' | 'upgrade'

export const REPOSITORY_ROOT = path.resolve(import.meta.dirname, '..', '..', '..')
export const SUITE_ROOT = path.resolve(import.meta.dirname, '..')
export const RESULTS = path.join(SUITE_ROOT, 'test-results')

interface KindSettings {
  port: number
  basePath: string
  overlays: string[]
}

const BASE_FILES = ['docker-compose.yml', 'tests/e2e/compose.e2e.yml']

export const KINDS: Record<Kind, KindSettings> = {
  seeded: { port: 18181, basePath: '', overlays: [] },
  fresh: { port: 18182, basePath: '', overlays: [] },
  subpath: { port: 18183, basePath: '/cms', overlays: ['tests/e2e/compose.subpath.yml'] },
  upgrade: { port: 18184, basePath: '', overlays: [] },
}

/** The seeded administrator (data-model *Seeded administrator*). Throwaway credentials. */
export const ADMINISTRATOR = {
  displayName: 'E2E Operator',
  email: 'operator@e2e.test',
  password: 'e2e test passphrase 2026',
} as const

/** What the admin sends on every state-changing call (002 research R-11). */
export const REQUEST_HEADER = { 'X-QuickBuilder-Request': '1' } as const

export const project = (kind: Kind) => `qb-e2e-${kind}`

/** `http://127.0.0.1:<port><basePath>`: where the admin and the API answer for this kind. */
export const origin = (kind: Kind) => `http://127.0.0.1:${KINDS[kind].port}${KINDS[kind].basePath}`


export interface Run {
  code: number
  output: string
}

export function run(file: string, args: string[], env: Record<string, string> = {}, cwd = REPOSITORY_ROOT): Promise<Run> {
  return new Promise((resolve) => {
    execFile(
      file,
      args,
      { cwd, env: { ...process.env, ...env }, maxBuffer: 64 * 1024 * 1024 },
      (error, stdout, stderr) => {
        const code = error ? (typeof error.code === 'number' ? error.code : 1) : 0
        resolve({ code, output: `${stdout}${stderr}` })
      },
    )
  })
}

/** The image under test: QB_E2E_IMAGE, which global setup always sets. */
export function imageUnderTest(): string {
  const image = process.env.QB_E2E_IMAGE
  if (!image) {
    throw new Error('QB_E2E_IMAGE is not set; global setup sets it before any installation starts.')
  }
  return image
}

export function compose(kind: Kind, args: string[], image = imageUnderTest()): Promise<Run> {
  const files = [...BASE_FILES, ...KINDS[kind].overlays].flatMap((f) => ['-f', f])
  return run('docker', ['compose', '-p', project(kind), ...files, ...args], {
    QUICKBUILDER_IMAGE: image,
    QUICKBUILDER_PORT: String(KINDS[kind].port),
  })
}

async function must(result: Promise<Run>, what: string): Promise<string> {
  const { code, output } = await result
  if (code !== 0) {
    throw new Error(`${what} failed (exit ${code}):\n${output}`)
  }
  return output
}

/** Starts the kind from nothing, volumes included: setup is a claim about a fresh installation. */
export async function start(kind: Kind, image = imageUnderTest()): Promise<void> {
  await compose(kind, ['down', '-v'], image)
  await must(compose(kind, ['up', '-d'], image), `docker compose up (${project(kind)})`)
}

/** Replaces the app's image, keeping the volumes, as docs/operators/upgrade.md has operators do. */
export async function upgradeTo(kind: Kind, image: string): Promise<void> {
  await must(compose(kind, ['up', '-d'], image), `docker compose up with ${image} (${project(kind)})`)
}

const sleep = (ms: number) => new Promise((resolve) => setTimeout(resolve, ms))

/** Waits for the readiness probe through the kind's own address, proxy included. */
export async function waitReady(kind: Kind, timeoutMs = 3 * 60_000): Promise<void> {
  const deadline = Date.now() + timeoutMs
  while (Date.now() < deadline) {
    try {
      const response = await fetch(`${origin(kind)}/health/ready`)
      if (response.status === 200) {
        return
      }
    } catch {
      // Not listening yet.
    }
    await sleep(2_000)
  }

  const logs = await compose(kind, ['logs', '--tail', '40', 'app'])
  throw new Error(`${project(kind)} never reported ready within ${timeoutMs / 1000} s. Recent logs:\n${logs.output}`)
}

/**
 * The setup code, read from the logs as an operator reads it (002 research R-09): compact
 * JSON, one event per line, the code in the one event that has a SetupCode property.
 */
export async function setupCode(kind: Kind): Promise<string> {
  const deadline = Date.now() + 60_000
  while (Date.now() < deadline) {
    const { output } = await compose(kind, ['logs', '--no-log-prefix', 'app'])
    const line = output.split('\n').find((l) => l.includes('"SetupCode"'))
    if (line) {
      return (JSON.parse(line.trim()) as { SetupCode: string }).SetupCode
    }
    await sleep(2_000)
  }
  throw new Error(`The setup code never appeared in ${project(kind)}'s logs.`)
}

export async function saveLogs(kind: Kind): Promise<void> {
  const { output } = await compose(kind, ['logs', '--no-color'])
  await mkdir(RESULTS, { recursive: true })
  await writeFile(path.join(RESULTS, `compose-${project(kind)}.log`), output)
}

export async function stop(kind: Kind): Promise<void> {
  await compose(kind, ['down', '-v'])
}

/** What the upgrade setup seeded before upgrading, for the upgrade test to look for. */
export const UPGRADE_SEED = path.join(RESULTS, '.upgrade.json')
