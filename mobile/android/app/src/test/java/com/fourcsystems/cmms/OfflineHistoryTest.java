package com.fourcsystems.cmms;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;

import java.util.Arrays;
import org.junit.Test;

public class OfflineHistoryTest {

    private static final String OFFLINE = "https://localhost/offline.html";
    private static final String APP = "https://cmms.example.com/";
    private static final String A = APP + "assets";
    private static final String B = APP + "work-orders";
    private static final String BROKEN = APP + "api/attachments/1/content";

    private final OfflineHistory history = new OfflineHistory(OFFLINE);

    private void offlineAtLaunch() {
        history.mainFrameFailed(APP);
        assertFalse(history.pageFinished(APP)); // the failed address's own error page
        assertFalse(history.pageFinished(OFFLINE));
    }

    @Test
    public void offlineAtLaunchBackClosesTheApp() {
        offlineAtLaunch();
        assertEquals(0, history.stepsBack(Arrays.asList(APP, OFFLINE), 1));
        assertEquals(0, history.stepsBack(Arrays.asList(OFFLINE), 0));
    }

    @Test
    public void tryAgainThatWorksClearsTheHistoryOnce() {
        offlineAtLaunch();
        assertTrue(history.pageFinished(APP));
        assertFalse(history.pageFinished(A));
    }

    @Test
    public void tryAgainStillOfflineBackStillClosesTheApp() {
        offlineAtLaunch();
        history.mainFrameFailed(APP);
        assertFalse(history.pageFinished(OFFLINE));
        assertEquals(0, history.stepsBack(Arrays.asList(APP, APP, OFFLINE), 2));
    }

    @Test
    public void aBrokenLinkInTheAppBackReturnsToThePageBefore() {
        assertFalse(history.pageFinished(A));
        assertFalse(history.pageFinished(B));
        history.mainFrameFailed(BROKEN);
        assertFalse(history.pageFinished(OFFLINE));
        assertEquals(-2, history.stepsBack(Arrays.asList(A, B, BROKEN, OFFLINE), 3));
        assertFalse(history.pageFinished(B)); // going back is not "Try again": the history stays
    }

    @Test
    public void aFailedAddressMissingFromTheHistoryIsNotSkippedTwice() {
        history.mainFrameFailed(BROKEN);
        history.pageFinished(OFFLINE);
        assertEquals(-1, history.stepsBack(Arrays.asList(A, B, OFFLINE), 2));
    }

    @Test
    public void earlierOfflinePagesAndFailuresAreSkipped() {
        history.mainFrameFailed(A);
        history.pageFinished(OFFLINE);
        history.mainFrameFailed(B);
        history.pageFinished(OFFLINE);
        assertEquals(-4, history.stepsBack(Arrays.asList(APP, A, OFFLINE, B, OFFLINE), 4));
    }

    @Test
    public void addressesThatLoadAgainAreNoLongerSkipped() {
        offlineAtLaunch();
        assertTrue(history.pageFinished(APP));
        history.mainFrameFailed(BROKEN);
        history.pageFinished(OFFLINE);
        assertEquals(-2, history.stepsBack(Arrays.asList(APP, BROKEN, OFFLINE), 2));
    }

    @Test
    public void recognisesTheOfflinePage() {
        assertTrue(history.isOfflinePage(OFFLINE));
        assertTrue(history.isOfflinePage(OFFLINE + "?retry=1"));
        assertFalse(history.isOfflinePage(APP));
        assertFalse(history.isOfflinePage(null));
        assertFalse(new OfflineHistory(null).isOfflinePage(OFFLINE));
    }
}
