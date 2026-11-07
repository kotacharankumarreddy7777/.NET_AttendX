document.addEventListener('DOMContentLoaded', function () {
    const fabBtn = document.getElementById('aiFabBtn');
    const panel = document.getElementById('aiPanel');
    const panelContent = document.getElementById('aiPanelContent');
    const closeBtn = document.getElementById('aiPanelClose');
    const resizer = document.querySelector('.ai-drawer-resizer');

    if (!panel || !fabBtn) return;

    let panelLoaded = false;
    let isDragging = false;
    let dragStartX, dragStartY, initialLeft, initialTop;
    let debounceTimer;
    let isPanelOpen = false;

    // Open/Close Panel - Robust state machine
    function togglePanel(show) {
        clearTimeout(debounceTimer);
        
        if (show) {
            isPanelOpen = true;
            panel.classList.remove('d-none');
            panel.style.visibility = 'visible';
            // Force reflow so transition works
            void panel.offsetWidth;
            panel.classList.add('ai-panel-visible');
            if (!panelLoaded) loadPanelContent();
        } else {
            isPanelOpen = false;
            panel.classList.remove('ai-panel-visible');
            debounceTimer = setTimeout(() => {
                panel.classList.add('d-none');
            }, 250);
        }
    }

    async function loadPanelContent() {
        panelContent.innerHTML = `
            <div class="skeleton-loader p-2">
                <div class="skeleton-box" style="height: 60px;"></div>
                <div class="skeleton-box mt-2" style="width: 80%; height: 18px;"></div>
                <div class="skeleton-box mt-2" style="width: 95%; height: 120px;"></div>
            </div>
        `;
        try {
            const response = await fetch('/Ai/Index?partial=true');
            const html = await response.text();
            panelContent.innerHTML = html;
            panelLoaded = true;
            bindChatForm();
        } catch (err) {
            panelContent.innerHTML = `<div class="alert alert-danger small m-2">Failed to load AI chat: ${err.message}</div>`;
        }
    }

    function bindChatForm() {
        const form = document.getElementById('aiPanelChatForm');
        const input = document.getElementById('aiPanelMessageInput');
        const container = panelContent.querySelector('.chat-messages-list');
        const typingIndicator = document.getElementById('aiTypingIndicator');
        const navAction = document.getElementById('aiNavAction');
        const navBtn = document.getElementById('aiNavBtn');

        if (!form || !container || !input) return;
        panelContent.scrollTop = panelContent.scrollHeight;

        if (navBtn) {
            navBtn.onclick = () => {
                const url = navBtn.getAttribute('data-target');
                if (url) window.location.href = url;
            };
        }

        form.onsubmit = async function (e) {
            e.preventDefault();
            const message = input.value.trim();
            if (!message) return;

            if (navAction) navAction.classList.add('d-none');
            
            const now = new Date().toLocaleTimeString('en-IN', { hour: '2-digit', minute: '2-digit' });
            const userBubble = document.createElement('div');
            userBubble.className = 'd-flex flex-column gap-1 mb-2 align-items-end';
            userBubble.innerHTML = `<div class="chat-bubble-out p-2.5 px-3 rounded-3 text-white shadow-sm" style="max-width: 85%; font-size: 0.9rem; background: var(--navy);"><small class="d-block text-white-50 mb-1" style="font-size: 0.65rem;">${now}</small>${message.replace(/</g, "&lt;")}</div>`;
            container.appendChild(userBubble);
            input.value = '';
            panelContent.scrollTop = panelContent.scrollHeight;

            if (typingIndicator) typingIndicator.classList.remove('d-none');

            try {
                const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
                const response = await fetch('/Ai/Chat', {
                    method: 'POST',
                    headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token },
                    body: JSON.stringify({ message: message })
                });
                const data = await response.json();
                if (typingIndicator) typingIndicator.classList.add('d-none');

                let replyText = data.success ? data.reply : data.message;
                let navUrl = null;

                if (replyText.includes('GO_TO:')) {
                    const parts = replyText.split('GO_TO:');
                    replyText = parts[0].trim();
                    navUrl = parts[1].trim();
                }

                const aiBubble = document.createElement('div');
                aiBubble.className = 'd-flex flex-column gap-1 mb-2 align-items-start';
                aiBubble.innerHTML = `<div class="chat-bubble-in p-2.5 px-3 rounded-3 bg-white shadow-sm border" style="max-width: 85%; font-size: 0.9rem;"><small class="d-block text-muted mb-1" style="font-size: 0.65rem;">AI Assistant</small>${replyText.replace(/</g, "&lt;").replace(/\n/g, '<br>')}</div>`;
                if (typingIndicator) container.parentNode.insertBefore(aiBubble, typingIndicator);
                else container.appendChild(aiBubble);

                if (navUrl && navBtn && navAction) {
                    navBtn.setAttribute('data-target', navUrl);
                    navAction.querySelector('button').innerHTML = `Go to Page <i class="bi bi-arrow-right-circle-fill ms-1"></i>`;
                    navAction.classList.remove('d-none');
                }
                panelContent.scrollTop = panelContent.scrollHeight;
            } catch (err) {
                if (typingIndicator) typingIndicator.classList.add('d-none');
                const errBubble = document.createElement('div');
                errBubble.className = 'd-flex flex-column gap-1 mb-2 align-items-start';
                errBubble.innerHTML = `<div class="chat-bubble-in p-2.5 px-3 rounded-3 bg-danger bg-opacity-10 shadow-sm border border-danger" style="max-width: 85%; font-size: 0.9rem; color: var(--danger);">Network error.</div>`;
                container.appendChild(errBubble);
            }
        };
    }

    // FAB Click - Robust toggle using isPanelOpen state
    fabBtn.addEventListener('click', (e) => {
        e.stopPropagation();
        togglePanel(!isPanelOpen);
    });

    if (closeBtn) {
        closeBtn.addEventListener('click', (e) => {
            e.stopPropagation();
            togglePanel(false);
        });
    }

    // Close on clicking outside
    document.addEventListener('click', (e) => {
        if (isPanelOpen && !panel.contains(e.target) && e.target !== fabBtn && !fabBtn.contains(e.target)) {
            togglePanel(false);
        }
    });

    // Draggable header functionality
    const header = panel.querySelector('.glass-drawer-header');
    if (header) {
        header.style.cursor = 'grab';
        header.addEventListener('mousedown', (e) => {
            isDragging = true; 
            dragStartX = e.clientX; 
            dragStartY = e.clientY; 
            initialLeft = panel.offsetLeft; 
            initialTop = panel.offsetTop;
            panel.style.position = 'fixed'; 
            panel.style.right = 'auto'; 
            panel.style.bottom = 'auto'; 
            header.style.cursor = 'grabbing';
        });
        document.addEventListener('mousemove', (e) => {
            if (!isDragging) return;
            panel.style.left = (initialLeft + (e.clientX - dragStartX)) + 'px';
            panel.style.top = (initialTop + (e.clientY - dragStartY)) + 'px';
        });
        document.addEventListener('mouseup', () => { isDragging = false; header.style.cursor = 'grab'; });

        header.addEventListener('touchstart', (e) => {
            isDragging = true; 
            dragStartX = e.touches[0].clientX; 
            dragStartY = e.touches[0].clientY; 
            initialLeft = panel.offsetLeft; 
            initialTop = panel.offsetTop;
            panel.style.position = 'fixed'; 
            panel.style.right = 'auto'; 
            panel.style.bottom = 'auto';
        }, {passive: true});
        document.addEventListener('touchmove', (e) => {
            if (!isDragging) return;
            panel.style.left = (initialLeft + (e.touches[0].clientX - dragStartX)) + 'px';
            panel.style.top = (initialTop + (e.touches[0].clientY - dragStartY)) + 'px';
        }, {passive: true});
        document.addEventListener('touchend', () => isDragging = false);
    }
});
