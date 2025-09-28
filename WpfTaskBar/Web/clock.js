
// 勤怠記録管理
const timeRecord = {
    clockInDate: null,
    clockOutDate: null,
    lastUpdateDate: null
};

// 時刻エリアの更新
function updateDateTime() {
    // 勤怠時刻の表示
    document.getElementById('startTimeValue').textContent = timeRecord.clockInDate ?
        timeRecord.clockInDate.toLocaleTimeString('ja-JP', { hour: '2-digit', minute: '2-digit', second: '2-digit' }) : '--:--';
    document.getElementById('endTimeValue').textContent = timeRecord.clockOutDate ?
        timeRecord.clockOutDate.toLocaleTimeString('ja-JP', { hour: '2-digit', minute: '2-digit', second: '2-digit' }) : '--:--';

    // 現在時刻と日付はJavaScript側で直接取得
    updateCurrentTime();

    // 日付変更チェック（午前4時を基準）
    checkDateChange();

    // 警告表示の更新
    updateWarnings();
}

// 日付変更チェック（DateTimeItem.csの機能を移植）
function checkDateChange() {
    const now = new Date();
    if (timeRecord.lastUpdateDate) {
        // 現在が午前4時以降で、前回更新時が午前4時前の場合、勤怠をリセット
        if (now.getHours() >= 4 && timeRecord.lastUpdateDate.getHours() < 4) {
            timeRecord.clockInDate = null;
            timeRecord.clockOutDate = null;
            console.log('日付が更新されました。出勤・退勤時刻をリセットします。');
        }
    }
    timeRecord.lastUpdateDate = now;
}

// 警告表示の更新
function updateWarnings() {
    // 出勤時刻の警告表示
    const startTimeElement = document.getElementById('startTime');
    const isStartTimeMissing = !timeRecord.clockInDate;
    if (isStartTimeMissing) {
        startTimeElement.classList.add('missing');
    } else {
        startTimeElement.classList.remove('missing');
    }

    // 退勤時刻の警告表示（19時以降で退勤していない場合）
    const endTimeElement = document.getElementById('endTime');
    const now = new Date();
    const isEndTimeMissingAfter19 = !timeRecord.clockOutDate && now.getHours() >= 19;
    if (isEndTimeMissingAfter19) {
        endTimeElement.classList.add('missing');
    } else {
        endTimeElement.classList.remove('missing');
    }
}

// 現在時刻を更新する関数
function updateCurrentTime() {
    const now = new Date();

    // 現在時刻（HH:MM:SS形式）
    const currentTime = now.toLocaleTimeString('ja-JP', {
        hour: '2-digit',
        minute: '2-digit',
        second: '2-digit'
    });

    // 現在日付（YYYY/MM/DD形式）
    const currentDate = now.toLocaleDateString('ja-JP', {
        year: 'numeric',
        month: '2-digit',
        day: '2-digit'
    });

    document.getElementById('currentTime').textContent = currentTime;
    document.getElementById('currentDate').textContent = currentDate;
}

// 時刻フォーマット関数
function formatTime(date) {
    return date.toLocaleTimeString('ja-JP', {
        month: 'numeric',
        day: 'numeric',
        hour: '2-digit',
        minute: '2-digit'
    });
}

// 初回の時刻表示
updateDateTime();

// 定期的に時刻情報を更新
setInterval(() => updateDateTime(), 100);

window.chrome?.webview?.addEventListener('message', function(event) {
    let data;

    if (typeof event.data === 'string') {
        data = JSON.parse(event.data);
    } else {
        data = event.data;
    }

    if (!data) {
        return;
    }
    
    if (data.type === 'clock_in_update') {
        console.log('clock_in_update');
        timeRecord.clockInDate = new Date(data.date);
    }

    if (data.type === 'clock_out_update') {
        console.log('clock_out_update');
        timeRecord.clockOutDate = new Date(data.date);
    }

    if (data.type === 'clock_clear') {
        console.log('clock_clear');
        timeRecord.clockInDate = null;
        timeRecord.clockOutDate = null;
    }
});
