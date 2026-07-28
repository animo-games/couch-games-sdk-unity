mergeInto(LibraryManager.library, {
    CGU_IsAvailable: function () {
        return typeof window !== 'undefined' && window.CouchGames ? 1 : 0;
    },

    CGU_Invoke: function (methodPtr, argsJsonPtr, objectPtr, callbackPtr, requestId) {
        var method = UTF8ToString(methodPtr);
        var argsJson = UTF8ToString(argsJsonPtr);
        var objectName = UTF8ToString(objectPtr);
        var callback = UTF8ToString(callbackPtr);

        function send(success, result, error) {
            var isPlatformResponse = result && typeof result === 'object' &&
                (Object.prototype.hasOwnProperty.call(result, 'success') ||
                 Object.prototype.hasOwnProperty.call(result, 'payload'));
            var platformSuccess = success && (!isPlatformResponse || result.success !== false);
            var platformError = error || (isPlatformResponse && result.error ? String(result.error) : '');
            var payload = isPlatformResponse ? result.payload : result;
            if (typeof payload === 'undefined')
                payload = null;
            SendMessage(objectName, callback, JSON.stringify({
                requestId: requestId,
                success: platformSuccess,
                error: platformError,
                payloadJson: JSON.stringify(payload),
                rawJson: JSON.stringify(typeof result === 'undefined' ? null : result)
            }));
        }

        try {
            var sdk = window.CouchGames;
            if (!sdk || typeof sdk[method] !== 'function') {
                send(false, null, 'CouchGames.' + method + ' is unavailable');
                return;
            }
            var args = JSON.parse(argsJson || '[]');
            Promise.resolve(sdk[method].apply(sdk, args))
                .then(function (result) { send(true, result, ''); })
                .catch(function (error) {
                    send(false, null, error && error.message ? error.message : String(error));
                });
        } catch (error) {
            send(false, null, error && error.message ? error.message : String(error));
        }
    },

    CGU_InitializeLobby: function (
        objectPtr,
        eventCallbackPtr,
        playersCallbackPtr,
        identityCallbackPtr,
        currentGameCallbackPtr) {
        var objectName = UTF8ToString(objectPtr);
        var eventCallback = UTF8ToString(eventCallbackPtr);
        var playersCallback = UTF8ToString(playersCallbackPtr);
        var identityCallback = UTF8ToString(identityCallbackPtr);
        var currentGameCallback = UTF8ToString(currentGameCallbackPtr);
        var sdk = typeof window !== 'undefined' ? window.CouchGames : null;
        var lobby = sdk && sdk.lobby;

        if (!lobby ||
            typeof lobby.onAnyEvent !== 'function' ||
            typeof lobby.onPlayersChanged !== 'function') {
            return 0;
        }

        lobby.onAnyEvent(function (eventName, data, senderUserId) {
            SendMessage(objectName, eventCallback, JSON.stringify({
                eventName: String(eventName || ''),
                dataJson: JSON.stringify(typeof data === 'undefined' ? null : data),
                senderUserId: senderUserId == null ? '' : String(senderUserId)
            }));
        });

        lobby.onPlayersChanged(function (players) {
            SendMessage(objectName, playersCallback, JSON.stringify({
                players: Array.isArray(players) ? players : []
            }));
        });

        try {
            var me = typeof lobby.getMe === 'function' ? lobby.getMe() : null;
            SendMessage(objectName, identityCallback, JSON.stringify(me == null ? null : me));
            var game = typeof lobby.getCurrentGame === 'function' ? lobby.getCurrentGame() : null;
            SendMessage(objectName, currentGameCallback, JSON.stringify(game == null ? null : game));
            if (typeof lobby.getLobbyPlayers === 'function') {
                SendMessage(objectName, playersCallback, JSON.stringify({
                    players: lobby.getLobbyPlayers() || []
                }));
            }
        } catch (error) {
            console.warn('[CouchGames Unity] Failed to read initial lobby state', error);
        }
        return 1;
    },

    CGU_LobbySendEvent: function (eventPtr, dataJsonPtr, targetJsonPtr) {
        var eventName = UTF8ToString(eventPtr);
        var dataJson = UTF8ToString(dataJsonPtr);
        var targetJson = UTF8ToString(targetJsonPtr);
        var lobby = window.CouchGames && window.CouchGames.lobby;
        if (!lobby || typeof lobby.sendEvent !== 'function') {
            console.warn('[CouchGames Unity] lobby.sendEvent is unavailable');
            return;
        }
        var target = JSON.parse(targetJson || '{}');
        if (!target || Object.keys(target).length === 0)
            target = null;
        lobby.sendEvent(eventName, JSON.parse(dataJson || 'null'), target);
    },

    CGU_LoadLatestSaveSync: function () {
        if (!window.CouchGames || typeof window.CouchGames.loadLatestSave !== 'function')
            return 0;
        var result = window.CouchGames.loadLatestSave();
        if (result == null)
            return 0;
        if (typeof result !== 'string')
            result = JSON.stringify(result);
        var size = lengthBytesUTF8(result) + 1;
        var buffer = _malloc(size);
        stringToUTF8(result, buffer, size);
        return buffer;
    },

    CGU_GetExperienceDateSync: function () {
        if (!window.CouchGames || typeof window.CouchGames.getExperienceDate !== 'function')
            return 0;
        var result = window.CouchGames.getExperienceDate();
        if (result == null)
            return 0;
        result = String(result);
        var size = lengthBytesUTF8(result) + 1;
        var buffer = _malloc(size);
        stringToUTF8(result, buffer, size);
        return buffer;
    },

    CGU_Free: function (pointer) {
        if (pointer)
            _free(pointer);
    }
});
