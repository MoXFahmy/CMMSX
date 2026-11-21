package com.fourcsystems.cmms;

import static org.junit.Assert.assertFalse;
import static org.junit.Assert.assertTrue;

import org.junit.Test;

public class OriginsTest {

    private static final String APP = "https://cmx.cworks.com.eg/tech/";

    @Test
    public void pagesOfTheCloudAppAreTheApp() {
        assertTrue(Origins.sameOrigin("https://cmx.cworks.com.eg/work-orders/1", APP));
        assertTrue(Origins.sameOrigin("https://CMX.cworks.com.eg:443/", APP));
    }

    @Test
    public void otherSitesAndLookAlikesAreNot() {
        assertFalse(Origins.sameOrigin("https://example.com/", APP));
        assertFalse(Origins.sameOrigin("http://cmx.cworks.com.eg/", APP));
        assertFalse(Origins.sameOrigin("https://cmx.cworks.com.eg.evil.test/", APP));
        assertFalse(Origins.sameOrigin("https://cmx.cworks.com.eg:8443/", APP));
        assertFalse(Origins.sameOrigin(null, APP));
        assertFalse(Origins.sameOrigin("not a url", APP));
    }
}
