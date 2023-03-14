define(['baseView', 'loading', 'alert', 'confirm', 'emby-input', 'emby-select', 'emby-button', 'emby-checkbox', 'emby-scroller', 'emby-select', 'flexStyles'], function (BaseView, loading, alert, confirm) {
    'use strict';

    function getTargetListHtml(targets) {

        var html = '';

        for (var i = 0, length = targets.length; i < length; i++) {

            var target = targets[i];
            html += '<div class="listItem listItem-border" data-id="' + target.Id + '">';

            html += '<i class="md-icon listItemIcon">folder</i>';
            html += '<div class="listItemBody two-line">';

            html += '<h3 class="listItemBodyText">';
            html += target.Name;
            html += '</h3>';
            html += '<div class="listItemBodyText secondary">';
            html += target.Path;
            html += '</div>';

            html += '</div>';

            html += '<button type="button" is="paper-icon-button-light" class="btnDeleteFolder listItemButton" data-id="' + target.Id + '"><i class="md-icon">delete</i></button>';

            html += '</div>';
        }

        return html;
    }

    function deleteFolder(instance, id) {

        confirm({
            title: 'Confirm Deletion',
            text: 'Are you sure you wish to remove this sync folder? All sync jobs will be deleted.'
        }).then(function () {
            ApiClient.ajax({

                type: "DELETE",
                url: ApiClient.getUrl("FolderSync/Folders/" + id)

            }).then(function () {
                loadConfig(instance);
            });
        });
    }

    function editFolder(instance, id) {

        ApiClient.getJSON(ApiClient.getUrl("FolderSync/Folders/" + id)).then(function (folder) {
            editFolderObject(instance, folder);
        });
    }

    function editFolderObject(instance, folder) {

        ApiClient.getUsers().then(function (users) {
            require(['dialogHelper', 'formDialogStyle', 'emby-checkbox', 'emby-input'], function (dialogHelper) {
                showFolderEditor(instance, folder, users, dialogHelper);
            });
        });
    }

    function loadUsers(context, account, users) {

        var html = '';

        html += '<h3 class="checkboxListLabel">Users</h3>';

        html += '<div class="paperList checkboxList checkboxList-paperList">';

        for (var i = 0, length = users.length; i < length; i++) {

            var user = users[i];

            var isChecked = account.EnableAllUsers || account.UserIds.indexOf(user.Id) !== -1;
            var checkedAttribute = isChecked ? ' checked="checked"' : '';

            html += '<label><input is="emby-checkbox" class="chkUser" data-id="' + user.Id + '" type="checkbox"' + checkedAttribute + ' />';
            html += '<span>' + user.Name + '</span></label>';
        }

        html += '</div>';

        context.querySelector('.userAccess').innerHTML = html;

        if (users.length) {
            context.querySelector('.userAccessListContainer').classList.remove('hide');
        } else {
            context.querySelector('.userAccessListContainer').classList.add('hide');
        }

        context.querySelector('.chkEnableAllUsers').checked = account.EnableAllUsers;

        context.querySelector('.chkEnableAllUsers').dispatchEvent(new CustomEvent('change', {}));
    }

    function showFolderEditor(instance, folder, users, dialogHelper) {
        var dialogOptions = {
            removeOnClose: true,
            scrollY: false
        };

        dialogOptions.size = 'small';

        var dlg = dialogHelper.createDialog(dialogOptions);

        dlg.classList.add('formDialog');

        var html = '';
        var title = folder.Id ? 'Edit Folder' : 'Add Folder';

        html += '<div class="formDialogHeader">';
        html += '<button is="paper-icon-button-light" class="btnCancel autoSize" tabindex="-1"><i class="md-icon">&#xE5C4;</i></button>';
        html += '<h3 class="formDialogHeaderTitle">';
        html += title;
        html += '</h3>';

        html += '</div>';

        html += '<div is="emby-scroller" data-horizontal="false" data-centerfocus="card" class="formDialogContent">';
        html += '<div class="scrollSlider">';
        html += '<form class="dialogContentInner dialog-content-centered newCollectionForm padded-left padded-right">';

        html += '<div class="inputContainer"><input class="txtName" type="text" required="required" is="emby-input" label="Display name:" /><div class="fieldDescription">Enter a name to be displayed within sync menus.</div></div>';

        html += '<div class="inputContainer"><input class="txtPath" type="text" required="required" is="emby-input" label="Path:" /></div>';

        html += '<div>\
                    <h2>User Access</h2>\
                    <label class="checkboxContainer">\
                        <input type="checkbox" is="emby-checkbox" class="chkEnableAllUsers" />\
                        <span>Grant access to all users</span>\
                    </label>\
                    <div class="userAccessListContainer">\
                        <div class="userAccess">\
                        </div>\
                    </div>\
                </div>';

        html += '<div class="formDialogFooter">';
        html += '<button is="emby-button" type="submit" class="raised button-submit block formDialogFooterItem"><span>Save</span></button>';
        html += '</div>';

        html += '</form>';
        html += '</div>';
        html += '</div>';

        dlg.innerHTML = html;

        dlg.querySelector('.chkEnableAllUsers').addEventListener('change', function () {

            if (this.checked) {
                dlg.querySelector('.userAccessListContainer').classList.add('hide');
            } else {
                dlg.querySelector('.userAccessListContainer').classList.remove('hide');
            }
        });

        loadUsers(dlg, folder, users);
        dlg.querySelector('.txtName').value = folder.Name || '';
        dlg.querySelector('.txtPath').value = folder.Path || '';

        dlg.querySelector('.btnCancel').addEventListener('click', function () {

            dialogHelper.close(dlg);
        });

        dlg.querySelector('form').addEventListener("submit", function (e) {

            loading.show();

            var form = this;

            var updatedFolder = Object.assign(folder, {
                Name: form.querySelector('.txtName').value,
                Path: form.querySelector('.txtPath').value,
                EnableAllUsers: form.querySelector('.chkEnableAllUsers').checked
            });

            updatedFolder.UserIds = updatedFolder.EnableAllUsers ?
                [] :
                Array.prototype.map.call(form.querySelectorAll('.chkUser:checked'), function (r) {

                    return r.getAttribute('data-id');

                });

            ApiClient.ajax({

                type: "POST",
                url: ApiClient.getUrl("FolderSync/Folders"),
                data: JSON.stringify(updatedFolder),
                contentType: "application/json"

            }).then(function () {

                dialogHelper.close(dlg);
                loadConfig(instance);

            }, function () {
                alert({
                    text: 'There was an error adding the path entered. Please ensure the path is valid and can be written to before proceeding.'
                });
                loading.hide();
            });

            e.preventDefault();
        });

        dialogHelper.open(dlg);
    }

    function loadPageData(instance, config) {

        instance.view.querySelector('.folderSyncTargets').innerHTML = getTargetListHtml(config.SyncAccounts);
    }

    function loadConfig(instance) {

        ApiClient.getPluginConfiguration("7cfbb821-e8fd-40ab-b64e-a7749386a6b2").then(function (config) {

            loadPageData(instance, config);
            loading.hide();
        });
    }

    function View(view, params) {
        BaseView.apply(this, arguments);

        var instance = this;

        view.querySelector('.btnAddFolder').addEventListener('click', function () {

            editFolderObject(instance, {
                EnableAllUsers: true,
                UserIds: []
            });
        });

        view.querySelector('.folderSyncTargets').addEventListener('click', function (e) {

            var listItem = e.target.closest('.listItem');
            if (listItem) {
                var id = listItem.getAttribute('data-id');

                var btnDeleteFolder = e.target.closest('.btnDeleteFolder');

                if (btnDeleteFolder) {
                    deleteFolder(instance, id);
                } else {
                    editFolder(instance, id);
                }
            }
        });
    }

    Object.assign(View.prototype, BaseView.prototype);

    View.prototype.onResume = function (options) {

        BaseView.prototype.onResume.apply(this, arguments);

        loadConfig(this);
    };

    return View;
});
