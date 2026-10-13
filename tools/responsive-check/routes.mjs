// The pages the responsive check visits, per role, and how their route parameters are found.
// Route parameters are read from the app's own JSON API with the role's sign-in cookie, so the
// check always uses ids that really exist in the running database.
import { readdirSync, readFileSync } from 'node:fs';
import { join } from 'node:path';

// A certificate is only issued when a course is finished; without one the verify page is checked
// with an unknown id, which shows its "certificate not found" state.
const UNKNOWN_CERTIFICATE_ID = '00000000-0000-0000-0000-000000000000';

function allLessons(course) {
    return (course.modules ?? []).flatMap(module => module.lessons ?? []);
}

/**
 * Each parameter is picked from the JSON of `apiPath`, fetched as `role`. An apiPath may use
 * parameters resolved earlier in this list. `fallback` is used when the pick finds nothing.
 */
export const PARAMETER_SOURCES = [
    { name: 'exerciseSlug', role: 'guest', apiPath: '/api/exercises', pick: page => page.items?.[0]?.slug },
    { name: 'pathId', role: 'guest', apiPath: '/api/paths', pick: paths => paths[0]?.id },
    { name: 'blogSlug', role: 'guest', apiPath: '/api/blog/summaries', pick: articles => articles[0]?.slug },
    // An enrolled course only, so its lessons open (check-pages enrols the student first when
    // needed); prefer a workout lesson (one with exercises).
    { name: 'courseId', role: 'student', apiPath: '/api/courses?pageSize=50', pick: page => page.items.find(course => course.isEnrolled)?.id },
    { name: 'lessonId', role: 'student', apiPath: '/api/courses/{courseId}', pick: course => (allLessons(course).find(lesson => lesson.exerciseCount > 0) ?? allLessons(course)[0])?.id },
    { name: 'verificationId', role: 'student', apiPath: '/api/certificates/my', pick: certificates => certificates[0]?.verificationId, fallback: UNKNOWN_CERTIFICATE_ID },
    { name: 'adminCourseId', role: 'admin', apiPath: '/api/admin/courses', pick: page => page.items?.[0]?.id },
    { name: 'adminModuleId', role: 'admin', apiPath: '/api/admin/modules/course/{adminCourseId}', pick: modules => modules[0]?.id },
    // The student form route takes the same id; it is listed by the admin API only.
    { name: 'assessmentId', role: 'admin', apiPath: '/api/admin/assessments', pick: page => page.items?.[0]?.id },
    { name: 'adminBlogId', role: 'admin', apiPath: '/api/admin/blog', pick: page => page.items?.[0]?.id },
    { name: 'adminPathId', role: 'admin', apiPath: '/api/admin/paths', pick: page => page.items?.[0]?.id },
    { name: 'adminTestimonialId', role: 'admin', apiPath: '/api/admin/testimonials', pick: page => page.items?.[0]?.id },
];

const SHARED_PUBLIC_PAGES = [
    '/',
    '/exercises',
    '/exercises/{exerciseSlug}',
    '/paths',
    '/paths/{pathId}',
    '/pricing',
    '/blog',
    '/blog/{blogSlug}',
    '/search?q=squat',
];

/** Pages per role; each role only visits the pages it is allowed to open. */
export const PAGES_BY_ROLE = {
    guest: [
        ...SHARED_PUBLIC_PAGES,
        '/login',
        '/register',
        '/forgot-password',
        '/privacy',
        '/terms',
        '/not-found',
        '/disabled',
        '/certificate/{verificationId}',
    ],
    student: [
        ...SHARED_PUBLIC_PAGES,
        '/courses',
        '/courses/{courseId}',
        '/lessons/{lessonId}',
        '/assessments',
        '/assessments/mine',
        '/assessments/{assessmentId}',
        '/billing',
        '/subscription/success',
        '/profile',
        '/sessions',
        '/chat',
        '/notifications',
        '/my-certificates',
    ],
    admin: [
        '/',
        '/admin/users',
        '/admin/courses',
        '/admin/courses/new',
        '/admin/courses/{adminCourseId}',
        '/admin/lessons/{adminModuleId}',
        '/admin/categories',
        '/admin/course-reviews',
        '/admin/assessments',
        '/admin/assessments/new',
        '/admin/assessments/{assessmentId}/edit',
        '/admin/assessments/{assessmentId}/submissions',
        '/admin/analytics',
        '/admin/blog',
        '/admin/blog/new',
        '/admin/blog/{adminBlogId}',
        '/admin/paths',
        '/admin/paths/new',
        '/admin/paths/{adminPathId}',
        '/admin/testimonials',
        '/admin/testimonials/new',
        '/admin/testimonials/{adminTestimonialId}',
        '/admin/sessions',
        '/profile',
        '/notifications',
        '/chat',
    ],
};

/** Replaces every {name} in a page template; returns null when a parameter is unknown. */
export function fillTemplate(template, parameters) {
    let missingParameter = false;
    const filled = template.replace(/\{(\w+)\}/g, (_match, name) => {
        if (parameters[name] === undefined) {
            missingParameter = true;
            return '';
        }

        return parameters[name];
    });

    return missingParameter ? null : filled;
}

/** "/courses/{CourseId:guid}" and "/courses/{courseId}?x=1" both become "/courses/{}". */
function normalizeRoute(route) {
    const withoutQuery = route.split('?')[0];
    return withoutQuery.replace(/\{[^}]+\}/g, '{}').toLowerCase();
}

/** Reads every @page route from the Razor pages folder. */
export function readPageRoutes(pagesFolder) {
    const routes = [];
    const razorFiles = readdirSync(pagesFolder).filter(fileName => fileName.endsWith('.razor'));

    for (const fileName of razorFiles) {
        const source = readFileSync(join(pagesFolder, fileName), 'utf8');
        for (const match of source.matchAll(/^@page\s+"([^"]+)"/gm)) {
            routes.push(match[1]);
        }
    }

    return routes;
}

/** The @page routes that no role's page list visits, so a new page cannot slip past the check. */
export function findUncoveredRoutes(pageRoutes) {
    const coveredRoutes = new Set(Object.values(PAGES_BY_ROLE).flat().map(normalizeRoute));
    return pageRoutes.filter(route => !coveredRoutes.has(normalizeRoute(route)));
}
