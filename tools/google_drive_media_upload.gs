/**
 * CrabSense — lưu ảnh/video vào folder My Drive.
 *
 * Folder: https://drive.google.com/drive/folders/1mgGZM-vmWg_Hqv3EOGtAiNKzS54nb5zR
 *
 * Deploy một lần:
 * 1. https://script.google.com → New project
 * 2. Dán file này vào Code.gs
 * 3. Đổi UPLOAD_SECRET cho trùng GOOGLE_DRIVE_WEBHOOK_SECRET trong CrabSenseBE/.env
 * 4. Deploy → New deployment → Type: Web app
 *    Execute as: Me
 *    Who has access: Anyone
 * 5. Copy Web app URL vào GOOGLE_DRIVE_WEBHOOK_URL rồi restart API
 *
 * POST JSON:
 *   { secret, filename, mimeType, dataBase64, folderPath, shareAnyone }
 */
var FOLDER_ID = '1mgGZM-vmWg_Hqv3EOGtAiNKzS54nb5zR';
var UPLOAD_SECRET = 'CHANGE_ME_TO_A_LONG_RANDOM_SECRET';
var MAX_BYTES = 12 * 1024 * 1024;

function doPost(e) {
  try {
    var body = JSON.parse(e.postData.contents);
    if (!UPLOAD_SECRET || UPLOAD_SECRET === 'CHANGE_ME_TO_A_LONG_RANDOM_SECRET') {
      return _json({ success: false, message: 'Server secret chưa cấu hình' });
    }
    if (!body.secret || body.secret !== UPLOAD_SECRET) {
      return _json({ success: false, message: 'Unauthorized' });
    }
    if (!body.dataBase64 || typeof body.dataBase64 !== 'string') {
      return _json({ success: false, message: 'Thiếu dataBase64' });
    }

    var filename = _safeFilename(body.filename);
    var mimeType = body.mimeType || 'application/octet-stream';
    var bytes = Utilities.base64Decode(body.dataBase64);
    if (bytes.length > MAX_BYTES) {
      return _json({ success: false, message: 'File quá lớn' });
    }

    var folder = _ensurePath(DriveApp.getFolderById(FOLDER_ID), body.folderPath);
    var blob = Utilities.newBlob(bytes, mimeType, filename);
    var file = folder.createFile(blob);
    if (body.shareAnyone !== false) {
      file.setSharing(DriveApp.Access.ANYONE_WITH_LINK, DriveApp.Permission.VIEW);
    }

    var id = file.getId();
    return _json({
      success: true,
      fileId: id,
      name: file.getName(),
      url: file.getUrl(),
      webContentLink: 'https://drive.google.com/uc?export=view&id=' + id
    });
  } catch (err) {
    return _json({ success: false, message: String(err) });
  }
}

function doGet() {
  return _json({
    ok: true,
    folderId: FOLDER_ID,
    message: 'CrabSense Drive media webhook (requires secret on POST)'
  });
}

function _ensurePath(root, relative) {
  var current = root;
  var parts = String(relative || '').split('/');
  for (var i = 0; i < parts.length; i++) {
    var name = String(parts[i] || '').replace(/[\\\/\?\*\|<>":]/g, '_').trim();
    if (!name) continue;
    var it = current.getFoldersByName(name);
    current = it.hasNext() ? it.next() : current.createFolder(name);
  }
  return current;
}

function _safeFilename(name) {
  var raw = (name || ('file_' + Date.now())).toString();
  raw = raw.replace(/[\\\/\?\*\|<>":]/g, '_');
  if (raw.length > 160) raw = raw.substring(0, 160);
  return raw;
}

function _json(obj) {
  return ContentService
    .createTextOutput(JSON.stringify(obj))
    .setMimeType(ContentService.MimeType.JSON);
}
