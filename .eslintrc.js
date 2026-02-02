/** @type {import('eslint').Linter.Config} */
module.exports = {
  root: true,
  extends: ['eslint:recommended', 'plugin:@typescript-eslint/recommended'],
  parser: '@typescript-eslint/parser',
  plugins: ['@typescript-eslint'],
  parserOptions: {
    ecmaVersion: 2022,
    sourceType: 'module',
  },
  env: {
    browser: true,
    es2022: true,
    node: true,
  },
  rules: {
    // Enforce no magic numbers (except common ones)
    'no-magic-numbers': [
      'warn',
      {
        ignore: [0, 1, -1, 2, 100, 1000],
        ignoreArrayIndexes: true,
        ignoreDefaultValues: true,
      },
    ],

    // TypeScript specific rules
    '@typescript-eslint/explicit-function-return-type': [
      'warn',
      {
        allowExpressions: true,
        allowTypedFunctionExpressions: true,
        allowHigherOrderFunctions: true,
      },
    ],
    '@typescript-eslint/no-explicit-any': 'error',
    '@typescript-eslint/no-unused-vars': [
      'error',
      {
        argsIgnorePattern: '^_',
        varsIgnorePattern: '^_',
      },
    ],
    '@typescript-eslint/no-non-null-assertion': 'warn',
    '@typescript-eslint/prefer-nullish-coalescing': 'off', // Requires type info
    '@typescript-eslint/prefer-optional-chain': 'warn',

    // General best practices
    'no-console': ['warn', { allow: ['warn', 'error'] }],
    'no-debugger': 'error',
    eqeqeq: ['error', 'always', { null: 'ignore' }],
    'no-var': 'error',
    'prefer-const': 'error',
    'prefer-template': 'warn',
    'no-throw-literal': 'error',

    // Code style
    curly: ['error', 'all'],
    'brace-style': ['error', '1tbs'],
    'no-multiple-empty-lines': ['error', { max: 1, maxEOF: 0 }],
    'no-trailing-spaces': 'error',
    'comma-dangle': ['error', 'always-multiline'],
  },
  ignorePatterns: [
    'node_modules/',
    'dist/',
    'build/',
    '.godot/',
    '*.min.js',
    'UAssetAPI/',
    'UAssetGUI/',
  ],
};
