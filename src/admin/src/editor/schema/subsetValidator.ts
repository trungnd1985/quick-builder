// A TypeScript port of the Pages module's SchemaSubsetValidator (011 research ED-08).
//
// The page schema in docs/contracts/ is the single definition of a valid document, and
// every validator of it must accept exactly what the server accepts. This interprets the
// same keyword subset, checks keywords in the same fixed order and reports the same first
// failure — keyword and JSON Pointer — so the golden files give one answer on both sides
// (documentValidator.spec.ts). Change the two together.

export type Json = null | boolean | number | string | Json[] | { [key: string]: Json }
export type JsonObject = { [key: string]: Json }

export interface SchemaFailure {
  /** A JSON Pointer to the value that failed. */
  location: string
  /** The keyword that failed, or `discriminator` when no `oneOf` branch names the value's type. */
  keyword: string
  message: string
}

/** The keywords this validator interprets or knowingly ignores. */
export const SUPPORTED_KEYWORDS: ReadonlySet<string> = new Set([
  '$schema', '$id', '$defs', '$ref', 'title', 'description', 'default',
  'type', 'properties', 'required', 'additionalProperties', 'const', 'enum',
  'pattern', 'minLength', 'maxLength', 'minimum', 'maximum',
  'items', 'minItems', 'maxItems', 'format', 'oneOf',
])

const UUID = /^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$/

export const isObject = (value: unknown): value is JsonObject =>
  typeof value === 'object' && value !== null && !Array.isArray(value)

/** A JSON Pointer reference token (RFC 6901): `~` and `/` escaped. */
export const escapeToken = (token: string): string => token.replace(/~/g, '~0').replace(/\//g, '~1')

/** Equality as JSON values: the comparison `const` and `enum` use. */
export function jsonEquals(a: unknown, b: unknown): boolean {
  if (a === b) return true
  if (Array.isArray(a) && Array.isArray(b)) {
    return a.length === b.length && a.every((item, i) => jsonEquals(item, b[i]))
  }
  if (isObject(a) && isObject(b)) {
    const keys = Object.keys(a)
    return keys.length === Object.keys(b).length && keys.every((k) => k in b && jsonEquals(a[k], b[k]))
  }
  return false
}

/** Length in code points, as the specification counts it, not UTF-16 units. */
export const codePointLength = (text: string): number => Array.from(text).length

export class SchemaSubsetValidator {
  private readonly patterns = new Map<string, RegExp>()
  private readonly root: JsonObject

  private constructor(root: JsonObject) {
    this.root = root
    this.compilePatterns(root)
  }

  /** Loads a schema, refusing one that uses anything outside the subset, as the server does. */
  static load(schema: JsonObject): SchemaSubsetValidator {
    const first = unsupportedKeywords(schema)[0]
    if (first) {
      throw new Error(`The schema uses '${first.keyword}' at ${first.location}, which the page schema validator does not support.`)
    }
    return new SchemaSubsetValidator(schema)
  }

  /** The first failure of `instance` against the schema, or null. */
  validate(instance: Json): SchemaFailure | null {
    return this.check(this.root, instance, '')
  }

  /** The schema of `$defs/<name>`. */
  definition(name: string): JsonObject {
    const defs = this.root.$defs
    const found = isObject(defs) ? defs[name] : undefined
    if (!isObject(found)) throw new Error(`The schema has no $defs/${name}.`)
    return found
  }

  /** Checks `instance` against any sub-schema of this schema, with `$ref`s resolved here. */
  checkAgainst(schema: Json, instance: Json, pointer = ''): SchemaFailure | null {
    return this.check(schema, instance, pointer)
  }

  /** Follows a `$ref` if the schema is one; otherwise returns it. */
  resolve(schema: Json): Json {
    return isObject(schema) && typeof schema.$ref === 'string' ? this.resolveRef(schema.$ref) : schema
  }

  /** The `type` constants of the `oneOf` branches of `$defs/<name>`. */
  branchTypes(name: string): string[] {
    const list = this.definition(name).oneOf
    if (!Array.isArray(list)) return []
    return list.flatMap((branch) => {
      const resolved = this.resolve(branch)
      const typeSchema = isObject(resolved) && isObject(resolved.properties) ? resolved.properties.type : undefined
      return isObject(typeSchema) && typeof typeSchema.const === 'string' ? [typeSchema.const] : []
    })
  }

  private check(schema: Json, instance: Json, pointer: string): SchemaFailure | null {
    if (schema === true) return null
    if (schema === false) return { location: pointer, keyword: 'false', message: 'No value is allowed here.' }
    if (!isObject(schema)) return null

    if (typeof schema.$ref === 'string') {
      const failure = this.check(this.resolveRef(schema.$ref), instance, pointer)
      if (failure) return failure
    }

    if (schema.type !== undefined && !matchesType(schema.type, instance)) {
      return { location: pointer, keyword: 'type', message: `Expected ${describeType(schema.type)}.` }
    }

    if ('const' in schema && !jsonEquals(schema.const, instance)) {
      return { location: pointer, keyword: 'const', message: `Expected ${JSON.stringify(schema.const)}.` }
    }

    if (Array.isArray(schema.enum) && !schema.enum.some((option) => jsonEquals(option, instance))) {
      return { location: pointer, keyword: 'enum', message: 'The value is not one of the allowed values.' }
    }

    if (Array.isArray(schema.oneOf)) {
      const failure = this.checkOneOf(schema.oneOf, instance, pointer)
      if (failure) return failure
    }

    if (Array.isArray(instance)) return this.checkArray(schema, instance, pointer)
    if (isObject(instance)) return this.checkObject(schema, instance, pointer)
    if (typeof instance === 'string') return this.checkString(schema, instance, pointer)
    if (typeof instance === 'number') return checkNumber(schema, instance, pointer)
    return null
  }

  /** `oneOf`, restricted to branches told apart by a `type` property's `const`. */
  private checkOneOf(branches: Json[], instance: Json, pointer: string): SchemaFailure | null {
    const discriminator = isObject(instance) && 'type' in instance ? instance.type : undefined

    for (const branch of branches) {
      const resolved = this.resolve(branch)
      const typeSchema = isObject(resolved) && isObject(resolved.properties) ? resolved.properties.type : undefined
      if (isObject(typeSchema) && 'const' in typeSchema && discriminator !== undefined && jsonEquals(typeSchema.const, discriminator)) {
        return this.check(branch, instance, pointer)
      }
    }

    return { location: pointer, keyword: 'discriminator', message: 'The value matches none of the allowed kinds.' }
  }

  private checkObject(schema: JsonObject, instance: JsonObject, pointer: string): SchemaFailure | null {
    if (Array.isArray(schema.required)) {
      for (const name of schema.required) {
        if (typeof name === 'string' && !(name in instance)) {
          return { location: pointer, keyword: 'required', message: `'${name}' is required.` }
        }
      }
    }

    const properties = isObject(schema.properties) ? schema.properties : undefined
    const hasAdditional = 'additionalProperties' in schema

    for (const [name, value] of Object.entries(instance)) {
      const childPointer = `${pointer}/${escapeToken(name)}`

      if (properties && name in properties) {
        const failure = this.check(properties[name], value, childPointer)
        if (failure) return failure
      } else if (hasAdditional) {
        if (schema.additionalProperties === false) {
          return { location: childPointer, keyword: 'additionalProperties', message: `'${name}' is not allowed here.` }
        }
        const failure = this.check(schema.additionalProperties, value, childPointer)
        if (failure) return failure
      }
    }

    return null
  }

  private checkArray(schema: JsonObject, instance: Json[], pointer: string): SchemaFailure | null {
    if (typeof schema.minItems === 'number' && instance.length < schema.minItems) {
      return { location: pointer, keyword: 'minItems', message: `At least ${schema.minItems} item(s) are required.` }
    }
    if (typeof schema.maxItems === 'number' && instance.length > schema.maxItems) {
      return { location: pointer, keyword: 'maxItems', message: `At most ${schema.maxItems} item(s) are allowed.` }
    }
    if (schema.items !== undefined) {
      for (let i = 0; i < instance.length; i++) {
        const failure = this.check(schema.items, instance[i], `${pointer}/${i}`)
        if (failure) return failure
      }
    }
    return null
  }

  private checkString(schema: JsonObject, instance: string, pointer: string): SchemaFailure | null {
    const length = codePointLength(instance)

    if (typeof schema.minLength === 'number' && length < schema.minLength) {
      return { location: pointer, keyword: 'minLength', message: `At least ${schema.minLength} character(s) are required.` }
    }
    if (typeof schema.maxLength === 'number' && length > schema.maxLength) {
      return { location: pointer, keyword: 'maxLength', message: `At most ${schema.maxLength} character(s) are allowed.` }
    }
    if (typeof schema.pattern === 'string' && !this.pattern(schema.pattern).test(instance)) {
      return { location: pointer, keyword: 'pattern', message: 'The value does not have the required form.' }
    }
    if (schema.format !== undefined && !UUID.test(instance)) {
      return { location: pointer, keyword: 'format', message: 'Expected a UUID.' }
    }
    return null
  }

  private resolveRef(reference: string): Json {
    const name = reference.slice('#/$defs/'.length).replace(/~1/g, '/').replace(/~0/g, '~')
    return this.definition(name)
  }

  private pattern(source: string): RegExp {
    let compiled = this.patterns.get(source)
    if (!compiled) {
      compiled = new RegExp(source, 'u')
      this.patterns.set(source, compiled)
    }
    return compiled
  }

  private compilePatterns(schema: Json): void {
    if (Array.isArray(schema)) {
      schema.forEach((item) => this.compilePatterns(item))
    } else if (isObject(schema)) {
      for (const [name, value] of Object.entries(schema)) {
        if (name === 'pattern' && typeof value === 'string') this.pattern(value)
        else if (name !== 'const' && name !== 'enum' && name !== 'default') this.compilePatterns(value)
      }
    }
  }
}

/** Every keyword outside the subset, with where it is. */
export function unsupportedKeywords(schema: Json, location = ''): { keyword: string; location: string }[] {
  if (!isObject(schema)) return []
  const found: { keyword: string; location: string }[] = []

  for (const [name, value] of Object.entries(schema)) {
    const here = `${location}/${escapeToken(name)}`

    if (!SUPPORTED_KEYWORDS.has(name)) {
      found.push({ keyword: name, location: here })
      continue
    }
    if (name === 'format' && value !== 'uuid') found.push({ keyword: `format: ${String(value)}`, location: here })
    if (name === '$ref' && !(typeof value === 'string' && value.startsWith('#/$defs/'))) {
      found.push({ keyword: `$ref: ${String(value)}`, location: here })
    }
    if (name === 'pattern' && typeof value === 'string' && ['(?<', '\\p{', '\\P{'].some((c) => value.includes(c))) {
      found.push({ keyword: `pattern: ${value} (not portable to ECMAScript)`, location: here })
    }

    let subschemas: [Json, string][] = []
    if ((name === 'properties' || name === '$defs') && isObject(value)) {
      subschemas = Object.entries(value).map(([key, sub]) => [sub, `${here}/${escapeToken(key)}`])
    } else if (name === 'items' || name === 'additionalProperties') {
      subschemas = [[value, here]]
    } else if (name === 'oneOf' && Array.isArray(value)) {
      subschemas = value.map((sub, i) => [sub, `${here}/${i}`])
    }
    for (const [sub, subLocation] of subschemas) found.push(...unsupportedKeywords(sub, subLocation))
  }

  return found
}

function matchesType(type: Json, instance: Json): boolean {
  if (Array.isArray(type)) return type.some((t) => matchesType(t, instance))
  switch (type) {
    case 'object':
      return isObject(instance)
    case 'array':
      return Array.isArray(instance)
    case 'string':
      return typeof instance === 'string'
    case 'number':
      return typeof instance === 'number'
    case 'integer':
      return typeof instance === 'number' && Number.isInteger(instance)
    case 'boolean':
      return typeof instance === 'boolean'
    case 'null':
      return instance === null
    default:
      return false
  }
}

const describeType = (type: Json): string => (Array.isArray(type) ? type.join(' or ') : String(type))

function checkNumber(schema: JsonObject, instance: number, pointer: string): SchemaFailure | null {
  if (typeof schema.minimum === 'number' && instance < schema.minimum) {
    return { location: pointer, keyword: 'minimum', message: `The value must be at least ${schema.minimum}.` }
  }
  if (typeof schema.maximum === 'number' && instance > schema.maximum) {
    return { location: pointer, keyword: 'maximum', message: `The value must be at most ${schema.maximum}.` }
  }
  return null
}
