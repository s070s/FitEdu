// Smoke: the admin blog editor loads Quill from the CDN on first open, under the
// Content-Security-Policy and with its integrity hashes (P03-10, P03-11), and falls
// back to a plain text box when the CDN cannot be reached. Neither is reachable from a
// WebApplicationFactory test: both need a real browser enforcing the policy.
import { test, expect, request } from '@playwright/test';
import { login, waitForCircuit, TEST_PASSWORD } from './helpers.mjs';

const ADMIN_EMAIL = 'admin@fitedu.local';
const BLOG_EDITOR_PATH = '/admin/blog/new';
const QUILL_CDN_PATTERN = 'https://cdn.jsdelivr.net/**';

// Global setup resets the students only; the seeded admin gets the same known password here.
test.beforeAll( async () => {
    const api = await request.newContext( {
        baseURL: 'https://localhost:7090',
        ignoreHTTPSErrors: true,
    } );

    try {
        const response = await api.post( '/api/auth/dev/reset-password', {
            data: { Email: ADMIN_EMAIL, NewPassword: TEST_PASSWORD },
        } );

        if ( !response.ok() ) {
            throw new Error(
                `dev password reset failed for ${ADMIN_EMAIL}: HTTP ${response.status()}. ` +
                'The endpoint is Development-only — make sure the server runs with ' +
                'ASPNETCORE_ENVIRONMENT=Development and the admin seed has run.' );
        }
    } finally {
        await api.dispose();
    }
} );

test( 'blog editor loads Quill from the CDN without a Content-Security-Policy violation', async ( { page } ) => {
    // Recorded in the page itself, so a violation on any full page load of the test is kept.
    await page.addInitScript( () => {
        window.__cspViolations = [];
        document.addEventListener( 'securitypolicyviolation', event => {
            window.__cspViolations.push( `${event.violatedDirective} ${event.blockedURI}` );
        } );
    } );

    await login( page, ADMIN_EMAIL );
    await page.goto( BLOG_EDITOR_PATH );
    await waitForCircuit( page );

    await expect( page.locator( '.ql-toolbar' ).first() ).toBeVisible( { timeout: 20_000 } );
    expect( await page.evaluate( () => window.__cspViolations ) ).toEqual( [] );
} );

test( 'blog editor falls back to a plain text box when the CDN is unreachable', async ( { page } ) => {
    await page.route( QUILL_CDN_PATTERN, route => route.abort() );

    await login( page, ADMIN_EMAIL );
    await page.goto( BLOG_EDITOR_PATH );
    await waitForCircuit( page );

    // The textarea quill-interop.js _useFallback adds.
    await expect( page.locator( 'textarea.form-control.font-monospace' ).first() ).toBeVisible( { timeout: 20_000 } );
    await expect( page.locator( '.ql-toolbar' ) ).toHaveCount( 0 );
} );
