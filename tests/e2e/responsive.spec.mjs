// Smoke: the key public pages fit a phone and a desktop screen — no sideways scrolling, no broken
// photos (lazy ones are scrolled into view first), and the phone tab bar only on small screens.
// The full sweep over every page, role and language is tools/responsive-check/check-pages.mjs.
import { test, expect } from '@playwright/test';
import { waitForCircuit } from './helpers.mjs';

const PUBLIC_PAGES = ['/', '/exercises', '/exercises/pushups', '/paths', '/pricing', '/blog', '/login', '/register'];

const SCREENS = [
    { name: 'phone', viewport: { width: 375, height: 812 }, isMobile: true, hasTouch: true, showsBottomNav: true },
    { name: 'desktop', viewport: { width: 1280, height: 800 }, isMobile: false, hasTouch: false, showsBottomNav: false },
];

/** Scrolls down one screen at a time so lazy images load, then waits for every visible image. */
async function scrollThroughPage( page ) {
    await page.evaluate( async () => {
        const pause = milliseconds => new Promise( resolve => setTimeout( resolve, milliseconds ) );
        while ( window.scrollY + window.innerHeight < document.documentElement.scrollHeight - 1 ) {
            window.scrollBy( 0, window.innerHeight );
            await pause( 120 );
        }
    } );
    await page.waitForFunction( () => [...document.images]
        .filter( image => image.checkVisibility() )
        .every( image => image.complete ), null, { timeout: 10_000 } );
}

for ( const screen of SCREENS ) {
    test.describe( `${screen.name} layout`, () => {
        test.use( { viewport: screen.viewport, isMobile: screen.isMobile, hasTouch: screen.hasTouch } );

        for ( const path of PUBLIC_PAGES ) {
            test( `${path} fits the ${screen.name} screen with working images`, async ( { page } ) => {
                await page.goto( path );
                await waitForCircuit( page );
                await scrollThroughPage( page );

                const layout = await page.evaluate( () => ( {
                    scrollWidth: document.documentElement.scrollWidth,
                    innerWidth: window.innerWidth,
                    brokenImages: [...document.images]
                        .filter( image => image.checkVisibility() && image.naturalWidth === 0 )
                        .map( image => image.currentSrc || image.src ),
                } ) );

                expect( layout.scrollWidth ).toBeLessThanOrEqual( layout.innerWidth );
                expect( layout.brokenImages ).toEqual( [] );
                if ( screen.showsBottomNav ) {
                    await expect( page.locator( '.bottom-nav' ) ).toBeVisible();
                } else {
                    await expect( page.locator( '.bottom-nav' ) ).toBeHidden();
                }
            } );
        }
    } );
}
