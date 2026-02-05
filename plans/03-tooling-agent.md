# Tooling Agent (DevOps)

**Phase**: 1 - Foundations
**Depends on**: Nothing (can start immediately)

## Scope

Linting, git hooks, editor config, code quality automation.

## Files to Create

```
(project root)
├── .editorconfig
├── lefthook.yml
├── .eslintrc.js
├── .prettierrc.json
├── .prettierignore
└── scripts/
    └── validate-readmes.sh

godot/scripts/
├── .editorconfig              # C# specific overrides
└── Directory.Build.props      # Analyzer configuration

svelte-ui/
├── .eslintrc.js               # Svelte-specific
├── tsconfig.json
└── .prettierrc.json           # Can extend root
```

## Tasks

### 1. EditorConfig (root `.editorconfig`)

```ini
root = true

[*]
indent_style = space
indent_size = 4
end_of_line = lf
charset = utf-8
trim_trailing_whitespace = true
insert_final_newline = true

[*.{ts,svelte,json,css,js}]
indent_size = 2

[*.md]
trim_trailing_whitespace = false

[*.{csproj,props,targets}]
indent_size = 2
```

- [ ] Create root EditorConfig
- [ ] Create C# specific EditorConfig in godot/scripts/

### 2. Lefthook Configuration

```yaml
# lefthook.yml
pre-commit:
  parallel: true
  commands:
    # C# checks
    dotnet-format:
      glob: "*.cs"
      run: dotnet format --include {staged_files} --verify-no-changes

    dotnet-build:
      glob: "*.cs"
      run: dotnet build --no-restore -warnaserror

    # TypeScript/Svelte checks
    eslint:
      glob: "*.{ts,svelte}"
      run: npx eslint {staged_files}

    prettier-check:
      glob: "*.{ts,svelte,json,css}"
      run: npx prettier --check {staged_files}

    svelte-check:
      glob: "*.svelte"
      run: npx svelte-check --fail-on-warnings

    typecheck:
      glob: "*.ts"
      run: npx tsc --noEmit

    # Documentation check
    readme-check:
      glob: "*/"
      run: ./scripts/validate-readmes.sh {staged_files}

pre-push:
  commands:
    tests:
      run: dotnet test && npm test
```

- [ ] Install Lefthook
- [ ] Create lefthook.yml
- [ ] Test pre-commit hooks
- [ ] Test pre-push hooks

### 3. ESLint Configuration

```javascript
// .eslintrc.js (root)
module.exports = {
  root: true,
  extends: [
    'eslint:recommended',
    '@typescript-eslint/recommended',
  ],
  parser: '@typescript-eslint/parser',
  plugins: ['@typescript-eslint'],
  parserOptions: {
    ecmaVersion: 2022,
    sourceType: 'module',
  },
  rules: {
    'no-magic-numbers': ['warn', { ignore: [0, 1, -1] }],
    '@typescript-eslint/explicit-function-return-type': 'warn',
    '@typescript-eslint/no-explicit-any': 'error',
  },
};
```

```javascript
// svelte-ui/.eslintrc.js
module.exports = {
  extends: [
    '../.eslintrc.js',
    'plugin:svelte/recommended',
  ],
  plugins: ['svelte'],
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
    // Custom rule to detect optimistic updates
    'no-restricted-syntax': [
      'error',
      {
        selector: 'AssignmentExpression[left.name=/^(selectedId|tree|properties)$/]',
        message: 'Do not mutate view model state directly. Forward to C# via IPC.',
      },
    ],
  },
};
```

- [ ] Create root ESLint config
- [ ] Create Svelte-specific ESLint config
- [ ] Add custom rule for backend-owned data pattern
- [ ] Install all ESLint dependencies

### 4. Prettier Configuration

```json
// .prettierrc.json
{
  "semi": true,
  "singleQuote": true,
  "trailingComma": "es5",
  "printWidth": 100,
  "tabWidth": 2,
  "useTabs": false,
  "plugins": ["prettier-plugin-svelte"],
  "overrides": [
    {
      "files": "*.svelte",
      "options": {
        "parser": "svelte"
      }
    }
  ]
}
```

```
# .prettierignore
node_modules/
.godot/
build/
dist/
*.min.js
```

- [ ] Create Prettier config
- [ ] Create Prettier ignore file
- [ ] Install Prettier plugins

### 5. C# Analyzers

```xml
<!-- godot/scripts/Directory.Build.props -->
<Project>
  <PropertyGroup>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="StyleCop.Analyzers" Version="1.2.*">
      <PrivateAssets>all</PrivateAssets>
      <IncludeAssets>runtime; build; native; contentfiles; analyzers</IncludeAssets>
    </PackageReference>
  </ItemGroup>
</Project>
```

- [ ] Create Directory.Build.props
- [ ] Configure StyleCop rules
- [ ] Configure Roslyn analyzers
- [ ] Test warnings as errors

### 6. README Validation Script

```bash
#!/bin/bash
# scripts/validate-readmes.sh

# Find directories without README.md
find . -type d \
  -not -path '*/node_modules/*' \
  -not -path '*/.git/*' \
  -not -path '*/.godot/*' \
  -not -path '*/build/*' \
  -not -path '*/dist/*' \
  | while read dir; do
    if [ ! -f "$dir/README.md" ]; then
      echo "Missing README.md in: $dir"
      exit 1
    fi
  done

echo "All directories have README.md"
```

- [ ] Create validation script
- [ ] Make it executable
- [ ] Test with missing READMEs

### 7. TypeScript Configuration

```json
// svelte-ui/tsconfig.json
{
  "extends": "./.svelte-kit/tsconfig.json",
  "compilerOptions": {
    "strict": true,
    "noImplicitAny": true,
    "noUnusedLocals": true,
    "noUnusedParameters": true,
    "noImplicitReturns": true,
    "noFallthroughCasesInSwitch": true,
    "exactOptionalPropertyTypes": true
  }
}
```

- [ ] Create strict TypeScript config
- [ ] Test compilation with strict mode

### 8. Documentation

- [ ] Create setup instructions in root README
- [ ] Document hook bypass procedures (for emergencies)
- [ ] Document linting rule exceptions

## Testing the Setup

### Pre-commit Test Cases

1. **Intentional lint error** → Hook should fail
2. **Missing README** → Hook should fail
3. **Magic number in code** → Warning should appear
4. **Optimistic update in Svelte** → Error should appear
5. **Clean commit** → Hook should pass

### Verification Steps

- [ ] Make commit with lint error, verify failure
- [ ] Make commit without README, verify failure
- [ ] Make clean commit, verify success
- [ ] Run full test suite, verify pre-push works

## Outputs for Other Agents

1. **Auto-formatting** - All code will be consistently formatted
2. **Style enforcement** - Consistent coding style
3. **README template** - Template for directory documentation
4. **Backend-owned data enforcement** - ESLint rule prevents frontend state

## Acceptance Criteria

- [ ] EditorConfig applies to all file types
- [ ] Lefthook runs on commit
- [ ] ESLint catches TypeScript/Svelte issues
- [ ] Prettier formats all frontend code
- [ ] StyleCop catches C# issues
- [ ] README validation works
- [ ] Custom ESLint rule detects optimistic updates
- [ ] All hooks can be bypassed if needed (--no-verify)

## Notes

- This workstream can start immediately (no dependencies)
- Other agents should pull these configs before starting
- If hooks are too slow, optimize glob patterns
- Keep rules reasonable - don't block productivity
