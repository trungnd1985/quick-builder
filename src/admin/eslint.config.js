import js from '@eslint/js'
import vue from 'eslint-plugin-vue'
import i18n from '@intlify/eslint-plugin-vue-i18n'
import tseslint from 'typescript-eslint'
import vueParser from 'vue-eslint-parser'

// SC-013 enforces two things automatically. This file is half of it: `no-raw-text` fails
// the build on any visible string written into a screen, which keeps FR-045a's promise
// honest — adding a language later must not mean reopening every component. The other
// half is the axe-core checks in tests/.
export default [
  { ignores: ['dist/**', 'node_modules/**', '*.config.js', '*.config.ts'] },
  js.configs.recommended,
  ...tseslint.configs.recommended,
  ...vue.configs['flat/recommended'],
  ...i18n.configs['flat/recommended'],
  {
    // Without this, vue-eslint-parser hands <script setup lang="ts"> to the default
    // parser, which chokes on the first generic it meets. The failure is quiet in the
    // worst way: the file errors out, so `no-raw-text` never inspects it, and the gate
    // that is supposed to prove no strings are baked into screens silently checks nothing.
    files: ['**/*.vue'],
    languageOptions: {
      parser: vueParser,
      parserOptions: {
        parser: tseslint.parser,
        extraFileExtensions: ['.vue'],
        ecmaVersion: 'latest',
        sourceType: 'module',
      },
    },
  },
  {
    settings: {
      'vue-i18n': {
        localeDir: './src/i18n/*.json',
        messageSyntaxVersion: '^11.0.0',
      },
    },
    rules: {
      // The rule that matters: no visible text outside the lookup.
      '@intlify/vue-i18n/no-raw-text': [
        'error',
        {
          ignoreNodes: ['md-icon', 'v-icon'],
          ignorePattern: '^[\\s\\p{P}]+$',
          ignoreText: [],
        },
      ],
      '@intlify/vue-i18n/no-missing-keys': 'error',

      // Off, not downgraded. The rule cannot see a key reached through a computed path,
      // and this codebase resolves error and empty-state messages that way — so it
      // reports keys that are genuinely used. A rule that cries wolf gets ignored, and
      // this one runs next to no-raw-text, which must not be.
      '@intlify/vue-i18n/no-unused-keys': 'off',
    },
  },
]
