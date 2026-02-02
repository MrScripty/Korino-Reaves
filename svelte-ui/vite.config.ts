import { sveltekit } from '@sveltejs/kit/vite';
import { defineConfig } from 'vite';

export default defineConfig({
    plugins: [sveltekit()],
    build: {
        // Target CEF's Chromium version
        target: 'chrome120',
        // Output to dist for CEF to serve
        outDir: 'dist',
        // Inline assets for simpler CEF loading
        assetsInlineLimit: 4096,
    },
    server: {
        // Dev server port
        port: 5173,
        // Allow access from Godot
        host: true,
    },
});
