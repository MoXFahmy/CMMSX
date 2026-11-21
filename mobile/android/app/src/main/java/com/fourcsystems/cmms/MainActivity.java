package com.fourcsystems.cmms;

import android.app.Activity;
import android.app.DownloadManager;
import android.content.ClipData;
import android.content.Intent;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.os.Environment;
import android.provider.MediaStore;
import android.webkit.CookieManager;
import android.webkit.URLUtil;
import android.webkit.ValueCallback;
import android.webkit.WebBackForwardList;
import android.webkit.WebResourceError;
import android.webkit.WebResourceRequest;
import android.webkit.WebResourceResponse;
import android.webkit.WebView;
import android.widget.Toast;
import androidx.activity.OnBackPressedCallback;
import androidx.activity.result.ActivityResult;
import androidx.activity.result.ActivityResultLauncher;
import androidx.activity.result.contract.ActivityResultContracts;
import androidx.core.content.FileProvider;
import com.getcapacitor.BridgeActivity;
import com.getcapacitor.BridgeWebChromeClient;
import com.getcapacitor.BridgeWebViewClient;
import com.getcapacitor.WebViewListener;
import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.InputStream;
import java.nio.charset.StandardCharsets;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.List;

/**
 * The CMX Android app: Capacitor shows the live cloud app (capacitor.config.ts); this adds what a web view lacks.
 * Downloads go to Android's download manager with the user's sign-in, files built in the page and printing go through
 * {@link AppBridge}, file inputs offer the camera, Back goes back in the app (see {@link OfflineHistory} for the offline
 * page), and the sign-in survives closing the app.
 */
public class MainActivity extends BridgeActivity {

    private ValueCallback<Uri[]> pendingFileCallback;
    private Uri pendingPhotoUri;
    private String shellScript;
    private OfflineHistory offlineHistory;

    private final ActivityResultLauncher<Intent> fileChooser =
        registerForActivityResult(new ActivityResultContracts.StartActivityForResult(), this::onFileChosen);

    @Override
    public void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        WebView webView = bridge.getWebView();
        offlineHistory = new OfflineHistory(bridge.getErrorUrl());

        // Capacitor's client shows the offline page when the main page fails; this also notes which address failed.
        bridge.setWebViewClient(new BridgeWebViewClient(bridge) {
            @Override
            public void onReceivedError(WebView view, WebResourceRequest request, WebResourceError error) {
                if (request.isForMainFrame()) offlineHistory.mainFrameFailed(request.getUrl().toString());
                super.onReceivedError(view, request, error);
            }

            @Override
            public void onReceivedHttpError(WebView view, WebResourceRequest request, WebResourceResponse response) {
                if (request.isForMainFrame()) offlineHistory.mainFrameFailed(request.getUrl().toString());
                super.onReceivedHttpError(view, request, response);
            }
        });

        // Added before the first page finishes loading, so window.CmmsAndroid is there from the start.
        webView.addJavascriptInterface(new AppBridge(this, webView), "CmmsAndroid");
        bridge.addWebViewListener(new WebViewListener() {
            @Override
            public void onPageLoaded(WebView view) {
                if (offlineHistory.pageFinished(view.getUrl())) view.clearHistory(); // "Try again" reached the app
                view.evaluateJavascript(shellScript(), null);
            }
        });

        webView.setDownloadListener(this::download);

        // Pinch to zoom (drawings, photos, small print), without the old on-screen zoom buttons.
        webView.getSettings().setSupportZoom(true);
        webView.getSettings().setBuiltInZoomControls(true);
        webView.getSettings().setDisplayZoomControls(false);

        webView.setWebChromeClient(new BridgeWebChromeClient(bridge) {
            @Override
            public boolean onShowFileChooser(WebView view, ValueCallback<Uri[]> callback, FileChooserParams params) {
                // Pages that ask for the camera themselves (capture) or for non-image files keep Capacitor's handling.
                if (params.isCaptureEnabled() || !acceptsImages(params)) return super.onShowFileChooser(view, callback, params);
                return showPhotoOrFileChooser(callback, params);
            }
        });

        getOnBackPressedDispatcher().addCallback(this, new OnBackPressedCallback(true) {
            @Override
            public void handleOnBackPressed() {
                WebView view = bridge.getWebView();
                int steps = view.canGoBack() ? -1 : 0;
                if (offlineHistory.isOfflinePage(view.getUrl())) {
                    WebBackForwardList list = view.copyBackForwardList();
                    List<String> urls = new ArrayList<>();
                    for (int i = 0; i < list.getSize(); i++) urls.add(list.getItemAtIndex(i).getUrl());
                    steps = offlineHistory.stepsBack(urls, list.getCurrentIndex());
                }
                if (steps < 0) {
                    view.goBackOrForward(steps);
                } else {
                    // Nothing to go back to in the app: let Android close it as usual.
                    setEnabled(false);
                    getOnBackPressedDispatcher().onBackPressed();
                    setEnabled(true);
                }
            }
        });
    }

    @Override
    public void onPause() {
        super.onPause();
        // Write the sign-in cookie to disk, so "Keep me signed in" survives the app being closed.
        CookieManager.getInstance().flush();
    }

    // ---- Downloads (attachments and other files served by the cloud app) ----

    private void download(String url, String userAgent, String contentDisposition, String mimeType, long contentLength) {
        String name = FileNames.fromContentDisposition(contentDisposition);
        if (name == null) name = URLUtil.guessFileName(url, null, mimeType);
        name = FileNames.safe(name, "CMX-file");
        if (url.startsWith("blob:") || url.startsWith("data:")) {
            // Files built in the page are saved by assets/cmms-shell.js; one that got past it is not lost silently.
            Toast.makeText(this, getString(R.string.could_not_download, name), Toast.LENGTH_LONG).show();
            return;
        }
        try {
            DownloadManager.Request request = new DownloadManager.Request(Uri.parse(url));
            String cookies = CookieManager.getInstance().getCookie(url);
            if (cookies != null) request.addRequestHeader("Cookie", cookies); // the file needs the user's sign-in
            request.addRequestHeader("User-Agent", userAgent);
            request.setTitle(name);
            if (mimeType != null) request.setMimeType(FileNames.plainType(mimeType));
            request.setNotificationVisibility(DownloadManager.Request.VISIBILITY_VISIBLE_NOTIFY_COMPLETED);
            if (Build.VERSION.SDK_INT >= Build.VERSION_CODES.Q) {
                request.setDestinationInExternalPublicDir(Environment.DIRECTORY_DOWNLOADS, name);
            } else {
                request.setDestinationInExternalFilesDir(this, Environment.DIRECTORY_DOWNLOADS, name);
            }
            ((DownloadManager) getSystemService(DOWNLOAD_SERVICE)).enqueue(request);
            Toast.makeText(this, getString(R.string.downloading, name), Toast.LENGTH_SHORT).show();
        } catch (Exception e) {
            Toast.makeText(this, getString(R.string.could_not_download, name), Toast.LENGTH_LONG).show();
        }
    }

    // ---- Photos and files for attachments: "Take photo" next to the usual file picker ----

    private static boolean acceptsImages(android.webkit.WebChromeClient.FileChooserParams params) {
        String[] types = params.getAcceptTypes();
        if (types == null || types.length == 0) return true;
        return Arrays.stream(types).anyMatch(t -> t == null || t.isEmpty() || t.equals("*/*") || t.startsWith("image/"));
    }

    private boolean showPhotoOrFileChooser(ValueCallback<Uri[]> callback, android.webkit.WebChromeClient.FileChooserParams params) {
        if (pendingFileCallback != null) pendingFileCallback.onReceiveValue(null);
        pendingFileCallback = callback;
        pendingPhotoUri = null;
        Intent pick = params.createIntent();
        if (params.getMode() == android.webkit.WebChromeClient.FileChooserParams.MODE_OPEN_MULTIPLE) {
            pick.putExtra(Intent.EXTRA_ALLOW_MULTIPLE, true);
        }
        Intent chooser = Intent.createChooser(pick, getString(R.string.add_file));
        try {
            File dir = new File(getCacheDir(), "photos");
            if (!dir.exists() && !dir.mkdirs()) throw new IllegalStateException("no photo folder");
            File photo = File.createTempFile("photo_", ".jpg", dir);
            pendingPhotoUri = FileProvider.getUriForFile(this, getPackageName() + ".fileprovider", photo);
            Intent camera = new Intent(MediaStore.ACTION_IMAGE_CAPTURE);
            camera.putExtra(MediaStore.EXTRA_OUTPUT, pendingPhotoUri);
            camera.addFlags(Intent.FLAG_GRANT_WRITE_URI_PERMISSION | Intent.FLAG_GRANT_READ_URI_PERMISSION);
            chooser.putExtra(Intent.EXTRA_INITIAL_INTENTS, new Intent[] { camera });
        } catch (Exception e) {
            pendingPhotoUri = null; // no camera option, the file picker still works
        }
        try {
            fileChooser.launch(chooser);
            return true;
        } catch (Exception e) {
            pendingFileCallback = null;
            return false;
        }
    }

    private void onFileChosen(ActivityResult result) {
        ValueCallback<Uri[]> callback = pendingFileCallback;
        pendingFileCallback = null;
        if (callback == null) return;
        Uri[] chosen = null;
        if (result.getResultCode() == Activity.RESULT_OK) {
            Intent data = result.getData();
            if (data != null && data.getClipData() != null) {
                ClipData clip = data.getClipData();
                chosen = new Uri[clip.getItemCount()];
                for (int i = 0; i < clip.getItemCount(); i++) chosen[i] = clip.getItemAt(i).getUri();
            } else if (data != null && data.getData() != null) {
                chosen = new Uri[] { data.getData() };
            } else if (pendingPhotoUri != null) {
                chosen = new Uri[] { pendingPhotoUri }; // the camera wrote the photo to the file we gave it
            }
        }
        callback.onReceiveValue(chosen);
    }

    // ---- The page script ----

    private String shellScript() {
        if (shellScript == null) {
            try (InputStream in = getAssets().open("cmms-shell.js")) {
                ByteArrayOutputStream out = new ByteArrayOutputStream();
                byte[] buffer = new byte[8192];
                for (int n; (n = in.read(buffer)) != -1; ) out.write(buffer, 0, n); // readAllBytes() needs Android 13
                shellScript = new String(out.toByteArray(), StandardCharsets.UTF_8);
            } catch (Exception e) {
                shellScript = "";
            }
        }
        return shellScript;
    }
}
