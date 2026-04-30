mergeInto(LibraryManager.library, {
    WebGL_OpenFilePicker: function (goNamePtr, spriteKeyPtr) {
        var goName = UTF8ToString(goNamePtr);
        var spriteKey = UTF8ToString(spriteKeyPtr);

        var fileInput = document.getElementById('__unity_art_file_input');
        if (!fileInput) {
            fileInput = document.createElement('input');
            fileInput.id = '__unity_art_file_input';
            fileInput.type = 'file';
            fileInput.accept = 'image/png,image/jpeg,image/webp';
            fileInput.style.display = 'none';
            document.body.appendChild(fileInput);
        }

        fileInput.value = '';
        fileInput.onchange = function (event) {
            var file = event.target.files[0];
            if (!file) return;

            var blobUrl = URL.createObjectURL(file);
            SendMessage(goName, 'OnImageSelected', spriteKey + '|' + blobUrl);
        };

        fileInput.click();
    },

    WebGL_RevokeBlobUrl: function (blobUrlPtr) {
        var blobUrl = UTF8ToString(blobUrlPtr);
        if (blobUrl.startsWith('blob:')) {
            URL.revokeObjectURL(blobUrl);
        }
    }
});
