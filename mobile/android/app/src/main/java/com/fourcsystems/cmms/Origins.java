package com.fourcsystems.cmms;

import java.net.URI;
import java.util.Locale;

/** Whether a page belongs to the cloud app (same scheme, host and port as capacitor.config.ts server.url). */
final class Origins {

    private Origins() {}

    static boolean sameOrigin(String url, String appUrl) {
        if (url == null || appUrl == null) return false;
        try {
            URI a = URI.create(url.trim());
            URI b = URI.create(appUrl.trim());
            if (a.getScheme() == null || a.getHost() == null || b.getHost() == null) return false;
            return a.getScheme().equalsIgnoreCase(b.getScheme())
                && a.getHost().toLowerCase(Locale.ROOT).equals(b.getHost().toLowerCase(Locale.ROOT))
                && port(a) == port(b);
        } catch (IllegalArgumentException e) {
            return false;
        }
    }

    private static int port(URI u) {
        if (u.getPort() != -1) return u.getPort();
        return "https".equalsIgnoreCase(u.getScheme()) ? 443 : "http".equalsIgnoreCase(u.getScheme()) ? 80 : -1;
    }
}
