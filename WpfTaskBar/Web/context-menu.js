// Exit機能
function exitApplication() {
    sendMessageToHost('exit_application');
}

// 開発者ツールを開く機能
function openDevTools() {
    sendMessageToHost('open_dev_tools');
}

// コンテキストメニューの制御
document.addEventListener('contextmenu', (e) => {
    if (e.target.closest('.task-item')) {
        return;
    }

    e.preventDefault();
    const contextMenu = document.getElementById('contextMenu');
    contextMenu.style.left = e.clientX + 'px';
    contextMenu.style.top = e.clientY + 'px';
    contextMenu.style.display = 'block';
});

document.addEventListener('click', () => {
    document.getElementById('contextMenu').style.display = 'none';
});
