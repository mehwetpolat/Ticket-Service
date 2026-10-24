#!/bin/sh
set -eu

primary=/model-roots/primary/models
secondary=/model-roots/secondary/models
link=/root/.ollama/models

# The app mounts the same directories, but Ollama may start first on a fresh
# run. Make sure both targets exist before validating the shared link.
mkdir -p "$primary" "$secondary"

if [ -L "$link" ]; then
    target=$(readlink "$link" || true)
    case "$target" in
        "$primary"|"$secondary")
            ;;
        *)
            echo "TickeTask: replacing unexpected Ollama models link target: $target" >&2
            rm -f "$link"
            ;;
    esac
elif [ -e "$link" ]; then
    if [ -d "$link" ]; then
        if [ -z "$(ls -A "$link" 2>/dev/null || true)" ]; then
            # A fresh Ollama start may create this as a real empty directory.
            rmdir "$link"
        else
            # Never delete model data. Keep it next to the link for recovery.
            backup="${link}.legacy-$(date +%Y%m%d%H%M%S)"
            echo "TickeTask: moving existing Ollama models directory to $backup" >&2
            mv "$link" "$backup"
        fi
    else
        echo "TickeTask: removing unexpected file at $link" >&2
        rm -f "$link"
    fi
fi

if [ ! -e "$link" ]; then
    ln -s "$primary" "$link"
fi

exec ollama serve
