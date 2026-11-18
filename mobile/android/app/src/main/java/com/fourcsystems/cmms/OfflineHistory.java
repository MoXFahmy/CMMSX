package com.fourcsystems.cmms;

import java.util.HashSet;
import java.util.List;
import java.util.Set;

/**
 * Keeps Back sensible around the offline page, which Capacitor shows (server.errorPath) after a page fails to load.
 * Back from the offline page returns to the last page that loaded, skipping the addresses that failed, or closes the
 * app when there is none (offline at launch); once "Try again" reaches the app, the failed history is forgotten.
 * Plain Java, see OfflineHistoryTest; MainActivity feeds it the web view's events.
 */
final class OfflineHistory {

    private final String offlineUrl;
    private final Set<String> failed = new HashSet<>();
    private boolean failing; // a page failed and the offline page has not finished yet (its error page may finish first)
    private boolean offlineShown;

    OfflineHistory(String offlineUrl) {
        this.offlineUrl = offlineUrl;
    }

    boolean isOfflinePage(String url) {
        return offlineUrl != null && url != null && url.startsWith(offlineUrl);
    }

    /** The main page at {@code url} failed to load (no connection, or an HTTP error). */
    void mainFrameFailed(String url) {
        failing = true;
        if (url != null) failed.add(url);
    }

    /** A page finished loading. Returns true when the back history should now be cleared ("Try again" worked). */
    boolean pageFinished(String url) {
        if (isOfflinePage(url)) {
            offlineShown = true;
            failing = false;
            return false;
        }
        if (failing) return false;
        failed.clear();
        boolean recovered = offlineShown;
        offlineShown = false;
        return recovered;
    }

    /**
     * Steps to pass to WebView.goBackOrForward from the offline page at {@code current} in {@code history} (the URLs of
     * the back/forward list), or 0 when no page before it loaded: close the app.
     */
    int stepsBack(List<String> history, int current) {
        for (int i = current - 1; i >= 0; i--) {
            String url = history.get(i);
            if (!isOfflinePage(url) && !failed.contains(url)) {
                offlineShown = false; // going back to a page the user had is not "Try again"
                return i - current;
            }
        }
        return 0;
    }
}
