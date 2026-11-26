// The suite's lint rules (specs/025-e2e-harness, E2E-08).
import js from '@eslint/js'
import tseslint from 'typescript-eslint'
import playwright from 'eslint-plugin-playwright'

export default tseslint.config(
  { ignores: ['node_modules/', 'test-results/', 'playwright-report/'] },
  js.configs.recommended,
  ...tseslint.configs.recommended,
  {
    ...playwright.configs['flat/recommended'],
    files: ['specs/**/*.ts', 'pages/**/*.ts', 'fixtures/**/*.ts', 'setup/**/*.ts'],
  },
  {
    files: ['specs/**/*.ts'],
    rules: {
      // Page objects assert inside their expect*() and waitLoaded() helpers.
      'playwright/expect-expect': ['warn', { assertFunctionPatterns: ['^expect', '^waitLoaded$'] }],
    },
  },
  {
    // Setup and teardown projects fail by throwing; they assert on nothing a member sees.
    files: ['setup/**/*.ts'],
    rules: { 'playwright/expect-expect': 'off', 'playwright/no-conditional-in-test': 'off' },
  },
  {
    files: ['**/*.ts'],
    rules: {
      // FR-023: elements are found as a member finds them, by role and name. A CSS or XPath
      // string breaks when markup or styling changes and checks nothing about accessibility.
      'no-restricted-syntax': [
        'error',
        {
          selector: "CallExpression[callee.property.name='locator'] > :matches(Literal, TemplateLiteral).arguments",
          message: 'Find elements with getByRole/getByLabel/getByText, or getByTestId for the IDs in README.md.',
        },
      ],
    },
  },
)
