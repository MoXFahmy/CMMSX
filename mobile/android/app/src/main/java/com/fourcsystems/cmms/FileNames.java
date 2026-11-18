package com.fourcsystems.cmms;

import java.io.ByteArrayOutputStream;
import java.nio.charset.Charset;
import java.util.ArrayList;
import java.util.List;
import java.util.Locale;

/**
 * Names for the files the app saves to Downloads: read from the server's Content-Disposition header (Android's own
 * URLUtil.guessFileName only understands "filename*" from Android 15 on) and made safe. Plain Java, see FileNamesTest.
 */
final class FileNames {

    private FileNames() {}

    /** The file name a Content-Disposition header gives, preferring the UTF-8 "filename*" (RFC 6266), or null. */
    static String fromContentDisposition(String header) {
        if (header == null) return null;
        String plain = null;
        String extended = null;
        for (String param : params(header)) {
            int eq = param.indexOf('=');
            if (eq < 0) continue;
            String key = param.substring(0, eq).trim().toLowerCase(Locale.ROOT);
            String value = param.substring(eq + 1).trim();
            if (key.equals("filename*")) extended = decodeExtended(value);
            else if (key.equals("filename")) plain = unquote(value);
        }
        if (notBlank(extended)) return extended.trim();
        return notBlank(plain) ? plain.trim() : null;
    }

    /** {@code name} without the characters a file name cannot have, or {@code fallback} when nothing is left. */
    static String safe(String name, String fallback) {
        String cleaned = name == null ? "" : name.replaceAll("[\\\\/:*?\"<>|\\p{Cntrl}]", "_").trim();
        return cleaned.isEmpty() ? fallback : cleaned;
    }

    private static boolean notBlank(String s) {
        return s != null && !s.trim().isEmpty();
    }

    // Splits on the semicolons outside quotes: a; b="x;y"; c -> [a, b="x;y", c].
    private static List<String> params(String header) {
        List<String> out = new ArrayList<>();
        StringBuilder current = new StringBuilder();
        boolean quoted = false;
        for (int i = 0; i < header.length(); i++) {
            char ch = header.charAt(i);
            if (quoted && ch == '\\' && i + 1 < header.length()) {
                current.append(ch).append(header.charAt(++i));
            } else if (ch == '"') {
                quoted = !quoted;
                current.append(ch);
            } else if (ch == ';' && !quoted) {
                out.add(current.toString());
                current.setLength(0);
            } else {
                current.append(ch);
            }
        }
        out.add(current.toString());
        return out;
    }

    private static String unquote(String value) {
        if (value.length() < 2 || value.charAt(0) != '"' || value.charAt(value.length() - 1) != '"') return value;
        StringBuilder out = new StringBuilder();
        for (int i = 1; i < value.length() - 1; i++) {
            char ch = value.charAt(i);
            if (ch == '\\' && i + 1 < value.length() - 1) ch = value.charAt(++i);
            out.append(ch);
        }
        return out.toString();
    }

    // RFC 5987: charset'language'percent-encoded bytes, e.g. UTF-8''Pump%20manual.pdf. Null when it cannot be read.
    private static String decodeExtended(String value) {
        int first = value.indexOf('\'');
        int second = first < 0 ? -1 : value.indexOf('\'', first + 1);
        if (second < 0) return null;
        try {
            Charset charset = Charset.forName(value.substring(0, first).trim());
            String encoded = value.substring(second + 1);
            ByteArrayOutputStream bytes = new ByteArrayOutputStream();
            for (int i = 0; i < encoded.length(); i++) {
                char ch = encoded.charAt(i);
                if (ch == '%') {
                    if (i + 2 >= encoded.length()) return null;
                    bytes.write(Integer.parseInt(encoded.substring(i + 1, i + 3), 16));
                    i += 2;
                } else {
                    bytes.write(ch);
                }
            }
            return new String(bytes.toByteArray(), charset);
        } catch (RuntimeException e) { // unknown charset or a bad %xx
            return null;
        }
    }
}
