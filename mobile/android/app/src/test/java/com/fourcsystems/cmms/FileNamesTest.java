package com.fourcsystems.cmms;

import static org.junit.Assert.assertEquals;
import static org.junit.Assert.assertNull;

import org.junit.Test;

public class FileNamesTest {

    @Test
    public void readsTheNameAspNetSendsForDownloads() {
        assertEquals("x.pdf", FileNames.fromContentDisposition("attachment; filename=x.pdf; filename*=UTF-8''x.pdf"));
    }

    @Test
    public void decodesSpacesAndAccentsFromTheUtf8Name() {
        assertEquals(
            "Pump manual é.pdf",
            FileNames.fromContentDisposition("attachment; filename=\"Pump manual _.pdf\"; filename*=UTF-8''Pump%20manual%20%C3%A9.pdf")
        );
    }

    @Test
    public void readsTheNameOfAFileShownInPlace() {
        assertEquals(
            "Pump manual 2.pdf",
            FileNames.fromContentDisposition("inline; filename=\"Pump manual 2.pdf\"; filename*=UTF-8''Pump%20manual%202.pdf")
        );
    }

    @Test
    public void readsAPlainOrQuotedName() {
        assertEquals("assets.csv", FileNames.fromContentDisposition("attachment; FileName=assets.csv"));
        assertEquals("a;b \"c\".txt", FileNames.fromContentDisposition("attachment; filename=\"a;b \\\"c\\\".txt\""));
    }

    @Test
    public void noNameGivesNull() {
        assertNull(FileNames.fromContentDisposition(null));
        assertNull(FileNames.fromContentDisposition("inline"));
        assertNull(FileNames.fromContentDisposition("attachment; filename=\"\""));
    }

    @Test
    public void aBrokenUtf8NameFallsBackToThePlainOne() {
        assertEquals("plain.pdf", FileNames.fromContentDisposition("attachment; filename=plain.pdf; filename*=UTF-8''bad%zz.pdf"));
    }

    @Test
    public void safeNamesKeepFoldersAndControlCharactersOut() {
        assertEquals(".._.._etc_passwd", FileNames.safe("../../etc/passwd", "file"));
        assertEquals("a_b.txt", FileNames.safe("a\nb.txt", "file"));
        assertEquals("file", FileNames.safe("  ", "file"));
        assertEquals("file", FileNames.safe(null, "file"));
    }
}
