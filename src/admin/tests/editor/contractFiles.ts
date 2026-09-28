// The contracts' golden files, read from docs/contracts where they live (011 ED-08).
import { readdirSync, readFileSync } from 'node:fs'
import { join, resolve } from 'node:path'

// Vitest runs from src/admin (jsdom's import.meta.url is not a file URL).
export const contractsDir = resolve(process.cwd(), '../../docs/contracts')

export const readJson = (path: string): unknown => JSON.parse(readFileSync(path, 'utf8'))

/** Every `*.json` in a directory except `expected.json`, sorted by name. */
export const jsonFiles = (directory: string): string[] =>
  readdirSync(directory)
    .filter((name) => name.endsWith('.json') && name !== 'expected.json')
    .sort()

export const pageExamples = join(contractsDir, 'page-schema', 'examples')

export const expectedFailures = (directory: string): Record<string, { rule?: string; code?: string; pointer: string }> => {
  const all = readJson(join(directory, 'expected.json')) as Record<string, never>
  return Object.fromEntries(Object.entries(all).filter(([name]) => !name.startsWith('$')))
}
