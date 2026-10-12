// Viewer-request CloudFront Function attached only to the default (S3) cache
// behavior. Rewrites extensionless paths (e.g. /forms/123) to /index.html so
// client-side routing works, without touching the /api/* and /hubs/* behaviors
// that route to the ALB — those must keep their real status codes (404 etc.)
// for the cookie-based auth session to work correctly.
//
// It also redirects www.<domain> to <domain>, so the app only ever runs on one
// origin (the refresh cookie is SameSite=Strict and CORS allows one origin).
// The /api/* and /hubs/* behaviors don't need it: an app loaded from the bare
// domain only calls the bare domain.
function handler(event) {
    const request = event.request;
    const uri = request.uri;
    const host = request.headers.host ? request.headers.host.value : '';

    if (host.startsWith('www.')) {
        return {
            statusCode: 301,
            statusDescription: 'Moved Permanently',
            headers: {
                location: { value: 'https://' + host.slice(4) + uri + queryString(request.querystring) }
            }
        };
    }

    if (!uri.includes('.')) {
        request.uri = '/index.html';
    }

    return request;
}

// CloudFront hands the query string over already parsed and still URL-encoded.
function queryString(querystring) {
    const parts = [];
    for (const key in querystring) {
        const param = querystring[key];
        const values = param.multiValue ? param.multiValue : [param];
        for (const v of values) {
            parts.push(v.value === '' ? key : key + '=' + v.value);
        }
    }
    return parts.length ? '?' + parts.join('&') : '';
}
