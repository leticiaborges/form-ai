// Viewer-request CloudFront Function attached only to the default (S3) cache
// behavior. Rewrites extensionless paths (e.g. /forms/123) to /index.html so
// client-side routing works, without touching the /api/* and /hubs/* behaviors
// that route to the ALB — those must keep their real status codes (404 etc.)
// for the cookie-based auth session to work correctly.
function handler(event) {
    const request = event.request;
    const uri = request.uri;

    if (!uri.includes('.')) {
        request.uri = '/index.html';
    }

    return request;
}
