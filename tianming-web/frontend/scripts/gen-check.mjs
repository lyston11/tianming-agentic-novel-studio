#!/usr/bin/env node
// Verify src/api/schema.d.ts matches what openapi-typescript produces from the
// committed ../backend/openapi.json. Never writes into the working tree — the
// candidate is generated into a temp directory and compared.
//
// This catches only frontend-side drift (someone hand-edited the generated
// types, or forgot to run gen:api after openapi.json changed). Backend-side
// drift — openapi.json falling behind the C# DTOs — is caught by
// ../backend/Scripts/export-openapi.sh --check, which needs the database stack
// running. Run both to cover the whole chain.
import { execFileSync } from 'node:child_process'
import { mkdtempSync, readFileSync, rmSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join, dirname, resolve } from 'node:path'
import { fileURLToPath } from 'node:url'

const frontendRoot = resolve(dirname(fileURLToPath(import.meta.url)), '..')
const spec = resolve(frontendRoot, '..', 'backend', 'openapi.json')
const committed = join(frontendRoot, 'src', 'api', 'schema.d.ts')

let scratch
try {
  scratch = mkdtempSync(join(tmpdir(), 'tm-gencheck-'))
  const candidate = join(scratch, 'schema.d.ts')

  execFileSync(
    process.execPath,
    [join(frontendRoot, 'node_modules', 'openapi-typescript', 'bin', 'cli.js'), spec, '-o', candidate],
    { stdio: 'pipe', cwd: frontendRoot },
  )

  const expected = readFileSync(candidate, 'utf8')
  const actual = readFileSync(committed, 'utf8')

  if (expected === actual) {
    console.log('schema.d.ts is up to date with ../backend/openapi.json.')
    process.exit(0)
  }

  console.error('schema.d.ts is out of date with ../backend/openapi.json.')
  console.error('Regenerate with: npm run gen:api')
  console.error('')

  const e = expected.split('\n')
  const a = actual.split('\n')
  let shown = 0
  for (let i = 0; i < Math.max(e.length, a.length) && shown < 20; i++) {
    if (e[i] !== a[i]) {
      console.error(`  line ${i + 1}:`)
      console.error(`    committed: ${a[i] ?? '<missing>'}`)
      console.error(`    generated: ${e[i] ?? '<missing>'}`)
      shown++
    }
  }
  process.exit(1)
} catch (error) {
  if (error?.status === 1 && error?.stdout === undefined) throw error
  if (error?.code === 'ENOENT') {
    console.error(`Missing input. Expected the spec at ${spec} and generated types at ${committed}.`)
    console.error('Export the spec first: ../backend/Scripts/export-openapi.sh')
    process.exit(1)
  }
  throw error
} finally {
  if (scratch) rmSync(scratch, { recursive: true, force: true })
}
