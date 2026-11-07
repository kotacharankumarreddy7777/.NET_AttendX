document.addEventListener('DOMContentLoaded', function () {
    const fabBtn = document.getElementById('aiFabBtn');
    const panel = document.getElementById('aiPanel');
    const panelContent = document.getElementById('aiPanelContent');
    const closeBtn = document.getElementById('aiPanelClose');
    const chatForm = document.getElementById('aiPanelChatForm');
    const chatInput = document.getElementById('aiPanelMessageInput');
    const resizer = document.querySelector('.ai-drawer-resizer');

    let panelLoaded = false;
    let isDragging = false;
    let dragStartX, dragStartY, initialLeft, initialTop;

    // Open/Close Panel
    function togglePanel(show) {
        if (show) {
            panel.classList.remove('d-none');
            // Trigger transition
            requestAnimationFrame(() => {
                panel.classList.add('ai-panel-visible');
            });
            if (!panelLoaded) {
                loadPanelContent();
            }
            setTimeout(() => chatInput.focus(), 300);
        } else {
            panel.classList.remove('ai-panel-visible');
            setTimeout(() => {
                panel.classList.add('d-none');
            }, 300);
        }
    }

    async function loadPanelContent() {
        // Show skeleton loader
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

        if (!form || !container) return;

        // Scroll to bottom initially
        panelContent.scrollTop = panelContent.scrollHeight;

        form.onsubmit = async function (e) {
            e.preventDefault();
            const message = input.value.trim();
            if (!message) return;

            const now = new Date().toLocaleTimeString('en-IN', { hour: '2-digit', minute: '2-digit' });
            
            // Add user message
            const userBubble = document.createElement('div');
            userBubble.className = 'd-flex flex-column gap-1 mb-2 align-items-end';
            userBubble.innerHTML = `<div class="chat-bubble-out p-2.5 px-3 rounded-3 text-white shadow-sm" style="max-width: 85%; font-size: 0.9rem; background: var(--navy);">
                <small class="d-block text-white-50 mb-1" style="font-size: 0.65rem;">${now}</small>
                ${message.replace(/</g, "&lt;")}
            </div>`;
            container.appendChild(userBubble);
            input.value = '';
            panelContent.scrollTop = panelContent.scrollHeight;

            // Show typing indicator
            if (typingIndicator) typingIndicator.classList.remove('d-none');

            try {
                const token = document.querySelector('#aiPanel input[name="__RequestVerificationToken"]')?.value || 
                              document.querySelector('input[name="__RequestVerificationToken"]').value;
                const response = await fetch('/Ai/Chat', {
                    method: 'POST',
                    headers: {
                        'Content-Type': 'application/json',
                        'RequestVerificationToken': token
                    },
                    body: JSON.stringify({ message: message })
                });
                const data = await response.json();
                if (typingIndicator) typingIndicator.classList.add('d-none');
                
                const aiBubble = document.createElement('div');
                aiBubble.className = 'd-flex flex-column gap-1 mb-2 align-items-start';
                aiBubble.innerHTML = `<div class="chat-bubble-in p-2.5 px-3 rounded-3 bg-white shadow-sm border" style="max-width: 85%; font-size: 0.9rem;">
                    <small class="d-block text-muted mb-1" style="font-size: 0.65rem;">AI Assistant</small>
                    ${data.success ? data.reply.replace(/</g, "&lt;").replace(/\n/g, '<br>') : data.message}
                </div>`;
                
                // Insert before typing indicator
                if (typingIndicator) container.parentNode.insertBefore(aiBubble, typingIndicator);
                else container.appendChild(aiBubble);
                
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

    if (fabBtn) {
        fabBtn.addEventListener('click', () => {
            const isHidden = panel.classList.contains('d-none');
            togglePanel(isHidden);
        });
    }

    if (closeBtn) {
        closeBtn.addEventListener('click', () => togglePanel(false));
    }

    // Close on clicking outside (optional, not on desktop interactive areas)
    document.addEventListener('click', (e) => {
        if (!panel.classList.contains('d-none') && !panel.contains(e.target) && e.target !== fabBtn && !fabBtn.contains(e.target)) {
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
            const dx = e.clientX - dragStartX;
            const dy = e.clientY - dragStartY;
            panel.style.left = (initialLeft + dx) + 'px';
            panel.style.top = (initialTop + dy) + 'px';
        });

        document.addEventListener('mouseup', () => {
            isDragging = false;
            header.style.cursor = 'grab';
        });

        // Touch drag for mobile
        header.addEventListener('touchstart', (e) => {
            isDragging = true;
            dragStartX = e.touches[0].clientX;
            dragStartY = e.touches[0].clientY;
            initialLeft = panel.offsetLeft;
            initialTop = panel.offsetTop;
            
            panel.style.position = 'fixed';
            panel.style.right = 'auto';
            panel.style.bottom = 'auto';
        });

        document.addEventListener('touchmove', (e) => {
            if (!isDragging) return;
            const dx = e.touches[0].clientX - dragStartX;
            const dy = e.touches[0].clientY - dragStartY;
            panel.style.left = (initialLeft + dx) + 'px';
            panel.style.top = (initialTop + dy) + 'px';
        });

        document.addEventListener('touchend', () => {
            isDragging = false;
        });
    }
});
