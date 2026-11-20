package com.fourcsystems.cmms;

import android.app.Activity;
import android.content.ContentResolver;
import android.content.ContentValues;
import android.content.Context;
import android.net.Uri;
import android.os.Build;
import android.os.Environment;
import android.print.PrintAttributes;
import android.print.PrintManager;
import android.provider.MediaStore;
import android.util.Base64;
import android.webkit.JavascriptInterface;
import android.webkit.WebView;
import android.widget.Toast;
import java.io.File;
import java.io.FileOutputStream;
import java.io.OutputStream;

/**
 * What the web app can ask of the phone, as {@code window.CmmsAndroid} (called by assets/cmms-shell.js): printing and
 * saving files the web app builds in the browser. Methods run on a WebView background thread.
 */
final class AppBridge {

    private final Activity activity;
    private final WebView webView;

    AppBridge(Activity activity, WebView webView) {
        this.activity = activity;
        this.webView = webView;
    }

    /** Opens Android's print dialog for the current page (a printer, or "Save as PDF"). */
    @JavascriptInterface
    public void print(String jobName) {
        final String name = jobName == null || jobName.trim().isEmpty() ? "CMX" : jobName.trim();
        activity.runOnUiThread(() -> {
            PrintManager printManager = (PrintManager) activity.getSystemService(Context.PRINT_SERVICE);
            printManager.print(name, webView.createPrintDocumentAdapter(name), new PrintAttributes.Builder().build());
        });
    }

    /** Saves a file built in the browser (base64) to Downloads and says so. */
    @JavascriptInterface
    public void saveFile(String fileName, String mimeType, String base64) {
        String name = FileNames.safe(fileName, "CMX-file");
        String mime = mimeType == null || mimeType.trim().isEmpty() ? "application/octet-stream" : mimeType;
        try {
            byte[] data = Base64.decode(base64, Base64.DEFAULT);
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
                ContentResolver resolver = activity.getContentResolver();
                ContentValues values = new ContentValues();
                values.put(MediaStore.Downloads.DISPLAY_NAME, name);
                values.put(MediaStore.Downloads.MIME_TYPE, mime);
                values.put(MediaStore.Downloads.IS_PENDING, 1);
                Uri uri = resolver.insert(MediaStore.Downloads.EXTERNAL_CONTENT_URI, values);
                if (uri == null) throw new IllegalStateException("no Downloads entry");
                try (OutputStream out = resolver.openOutputStream(uri)) {
                    if (out == null) throw new IllegalStateException("cannot write");
                    out.write(data);
                }
                values.clear();
                values.put(MediaStore.Downloads.IS_PENDING, 0);
                resolver.update(uri, values, null, null);
                toast(activity.getString(R.string.saved_to_downloads, name));
            } else {
                // Android 7–9: the app's own Downloads folder needs no storage permission.
                File dir = activity.getExternalFilesDir(Environment.DIRECTORY_DOWNLOADS);
                try (FileOutputStream out = new FileOutputStream(new File(dir, name))) {
                    out.write(data);
                }
                toast(activity.getString(R.string.saved_to_app_folder, name));
            }
        } catch (Exception e) {
            toast(activity.getString(R.string.could_not_save, name));
        }
    }

    /** A file built in the page could not be read (assets/cmms-shell.js): say so rather than do nothing. */
    @JavascriptInterface
    public void couldNotSave(String fileName) {
        toast(activity.getString(R.string.could_not_save, FileNames.safe(fileName, "CMX-file")));
    }

    private void toast(String message) {
        activity.runOnUiThread(() -> Toast.makeText(activity, message, Toast.LENGTH_LONG).show());
    }
}
