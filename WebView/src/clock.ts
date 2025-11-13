import { sendMessageToHost } from './network';
import type { TimeRecord, MessageData } from './types';

// 勤怠記録管理
const timeRecord: TimeRecord = {
  clockInDate: null,
  clockOutDate: null
};

// 時刻エリアの更新
function updateDateTime(): void {
  // 勤怠時刻の表示
  const startTimeValue = document.getElementById('startTimeValue');
  const endTimeValue = document.getElementById('endTimeValue');

  if (startTimeValue) {
    startTimeValue.textContent = timeRecord.clockInDate
      ? timeRecord.clockInDate.toLocaleTimeString('ja-JP', { hour: '2-digit', minute: '2-digit', second: '2-digit' })
      : '--:--';
  }

  if (endTimeValue) {
    endTimeValue.textContent = timeRecord.clockOutDate
      ? timeRecord.clockOutDate.toLocaleTimeString('ja-JP', { hour: '2-digit', minute: '2-digit', second: '2-digit' })
      : '--:--';
  }

  // 現在時刻と日付はJavaScript側で直接取得
  updateCurrentTime();

  // 日付変更チェック（午前4時を基準）
  checkDateChange();

  // 警告表示の更新
  updateWarnings();
}

// 日付変更チェック（DateTimeItem.csの機能を移植）
function checkDateChange(): void {
  const now = new Date();

  // 現在時刻の「業務日」を取得（午前4時を基準とする）
  const getCurrentBusinessDay = (date: Date): Date => {
    const businessDay = new Date(date);
    if (businessDay.getHours() < 4) {
      // 午前4時前なら前日の業務日とする
      businessDay.setDate(businessDay.getDate() - 1);
    }
    // 時刻を0:00:00にして日付のみで比較
    businessDay.setHours(0, 0, 0, 0);
    return businessDay;
  };

  const currentBusinessDay = getCurrentBusinessDay(now);

  // clockInDateが存在し、業務日が異なっていたらnullにする
  if (timeRecord.clockInDate) {
    const clockInBusinessDay = getCurrentBusinessDay(timeRecord.clockInDate);
    if (clockInBusinessDay.getTime() !== currentBusinessDay.getTime()) {
      timeRecord.clockInDate = null;
      console.log('出勤時刻が昨日の業務日のためリセットします。');
    }
  }

  // clockOutDateが存在し、業務日が異なっていたらnullにする
  if (timeRecord.clockOutDate) {
    const clockOutBusinessDay = getCurrentBusinessDay(timeRecord.clockOutDate);
    if (clockOutBusinessDay.getTime() !== currentBusinessDay.getTime()) {
      timeRecord.clockOutDate = null;
      console.log('退勤時刻が昨日の業務日のためリセットします。');
    }
  }
}

// 警告表示の更新
function updateWarnings(): void {
  // 出勤時刻の警告表示
  const startTimeElement = document.getElementById('startTime');
  const isStartTimeMissing = !timeRecord.clockInDate;
  if (startTimeElement) {
    if (isStartTimeMissing) {
      startTimeElement.classList.add('missing');
    } else {
      startTimeElement.classList.remove('missing');
    }
  }

  // 退勤時刻の警告表示（19時以降で退勤していない場合）
  const endTimeElement = document.getElementById('endTime');
  const now = new Date();
  const isEndTimeMissingAfter19 = !timeRecord.clockOutDate && now.getHours() >= 19;
  if (endTimeElement) {
    if (isEndTimeMissingAfter19) {
      endTimeElement.classList.add('missing');
    } else {
      endTimeElement.classList.remove('missing');
    }
  }
}

// 現在時刻を更新する関数
function updateCurrentTime(): void {
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

  const currentTimeElement = document.getElementById('currentTime');
  const currentDateElement = document.getElementById('currentDate');

  if (currentTimeElement) {
    currentTimeElement.textContent = currentTime;
  }

  if (currentDateElement) {
    currentDateElement.textContent = currentDate;
  }
}

// 起動時に時刻記録の状態を取得（WebView2経由）
function loadTimeRecordStatus(): void {
  try {
    // network.jsのsendMessageToHostを使用してC#側にリクエストを送信
    sendMessageToHost('request_time_record_status');
    console.log('時刻記録の状態をリクエストしました');
  } catch (error) {
    console.error('時刻記録の状態取得リクエスト中にエラーが発生しました:', error);
  }
}

// メッセージリスナーの設定
export function setupClockListeners(): void {
  window.chrome?.webview?.addEventListener('message', function(event) {
    let data: MessageData;

    if (typeof event.data === 'string') {
      data = JSON.parse(event.data);
    } else {
      data = event.data;
    }

    if (!data) {
      return;
    }

    // 起動時の時刻記録状態の受信
    if (data.type === 'time_record_status_response') {
      // ClockInDateとClockOutDateを設定
      if (data.clock_in_date && data.clock_in_date !== '0001-01-01T00:00:00') {
        timeRecord.clockInDate = new Date(data.clock_in_date as string);
      }

      if (data.clock_out_date && data.clock_out_date !== '0001-01-01T00:00:00') {
        timeRecord.clockOutDate = new Date(data.clock_out_date as string);
      }

      console.log('時刻記録の状態を受信しました:', data);

      updateDateTime();

      // 定期的に時刻情報を更新
      setInterval(() => updateDateTime(), 100);
    }

    if (data.type === 'clock_in_update') {
      timeRecord.clockInDate = new Date(data.date as string);
    }

    if (data.type === 'clock_out_update') {
      timeRecord.clockOutDate = new Date(data.date as string);
    }

    if (data.type === 'clock_clear') {
      timeRecord.clockInDate = null;
      timeRecord.clockOutDate = null;
    }
  });

  // 起動時に時刻記録の状態を取得
  loadTimeRecordStatus();
}
