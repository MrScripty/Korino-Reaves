import type { Reroute } from '@sveltejs/kit';

// When loaded via file:// protocol, the URL path includes "index.html"
// which SvelteKit doesn't recognize as a route. Remap it to root.
export const reroute: Reroute = ({ url }) => {
	if (url.pathname.endsWith('/index.html')) {
		return '/';
	}
};
