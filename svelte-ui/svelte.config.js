import adapter from '@sveltejs/adapter-static';
import { vitePreprocess } from '@sveltejs/vite-plugin-svelte';

/** @type {import('@sveltejs/kit').Config} */
const config = {
    preprocess: vitePreprocess(),

    kit: {
        paths: {
            relative: true
        },
        adapter: adapter({
            // Build to dist for CEF
            pages: 'dist',
            assets: 'dist',
            precompress: false,
            strict: true
        }),
        alias: {
            '$lib': './src/lib',
            '$components': './src/lib/components',
            '$bridge': './src/lib/bridge',
            '$view-models': './src/lib/view-models'
        }
    }
};

export default config;
