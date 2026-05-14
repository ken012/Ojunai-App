#!/usr/bin/env bash
#
# Install repo-tracked git hooks into .git/hooks/. Run once after cloning.
# Re-running is safe — it overwrites any existing symlinks of the same name.
#
# Usage: ./scripts/install-hooks.sh

set -euo pipefail

REPO_ROOT="$(git rev-parse --show-toplevel)"
HOOKS_DIR="$REPO_ROOT/.git/hooks"
SOURCE_DIR="$REPO_ROOT/scripts/git-hooks"

if [ ! -d "$HOOKS_DIR" ]; then
    echo "✘ $HOOKS_DIR doesn't exist — are you in a git checkout?"
    exit 1
fi

for hook in "$SOURCE_DIR"/*; do
    name="$(basename "$hook")"
    target="$HOOKS_DIR/$name"
    # Make the source executable in case git stripped the +x bit.
    chmod +x "$hook"
    # Symlink so the working tree's hook is always the live one; no risk of an
    # out-of-date copy in .git/hooks/.
    ln -sf "$hook" "$target"
    echo "✓ installed $name → $hook"
done

echo
echo "Done. To bypass a hook (rare): git commit --no-verify"
