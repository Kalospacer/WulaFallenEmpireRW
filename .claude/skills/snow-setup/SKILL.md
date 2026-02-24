---
name: snow-setup
description: Setup snow-ai CLI tool - checks Node/npm, cleans cache, and installs snow-ai globally
license: MIT
compatibility: opencode
---

# Snow Setup Skill

When the user invokes `/snow-setup`, execute the following commands to set up snow-ai:

## Commands to Run (in order)

```bash
npm config get prefix
```

```bash
node -v
```

```bash
npm -v
```

```bash
npm cache clean --force
```

```bash
npm install -g snow-ai@latest --force
```

```bash
snow --version
```

## Instructions

1. Run all commands sequentially
2. Show the output of each command to the user
3. If any command fails, stop and report the error
4. On success, confirm that snow-ai is ready to use
