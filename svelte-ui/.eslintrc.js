/** @type {import('eslint').Linter.Config} */
module.exports = {
  extends: ['../.eslintrc.js', 'plugin:svelte/recommended'],
  plugins: ['svelte'],
  parserOptions: {
    extraFileExtensions: ['.svelte'],
  },
  overrides: [
    {
      files: ['*.svelte'],
      parser: 'svelte-eslint-parser',
      parserOptions: {
        parser: '@typescript-eslint/parser',
      },
    },
  ],
  rules: {
    // Svelte-specific rules
    'svelte/no-at-html-tags': 'warn',
    'svelte/no-unused-svelte-ignore': 'error',
    'svelte/valid-compile': 'error',

    // Custom rules to enforce backend-owned data pattern
    // These patterns detect direct state mutations that should go through IPC
    'no-restricted-syntax': [
      'error',
      {
        // Prevent direct assignment to known view model state variables
        selector:
          "AssignmentExpression[left.type='Identifier'][left.name=/^(selectedId|tree|properties|assetData|expandedNodes|searchResults)$/]",
        message:
          'Do not mutate view model state directly. Forward actions to C# backend via IPC and let the backend push state updates.',
      },
      {
        // Prevent using .push(), .pop(), .splice() etc on view model arrays
        selector:
          "CallExpression[callee.property.name=/^(push|pop|shift|unshift|splice|fill|reverse|sort)$/][callee.object.name=/^(tree|properties|searchResults|assetData)$/]",
        message:
          'Do not mutate view model arrays directly. Request changes via IPC and let the backend push the updated array.',
      },
    ],

    // Relax console warnings for Svelte components (IPC uses console.log)
    'no-console': ['warn', { allow: ['warn', 'error', 'log'] }],
  },
  settings: {
    svelte: {
      ignoreWarnings: [
        '@typescript-eslint/no-unused-vars', // Handled by TypeScript
      ],
    },
  },
};
