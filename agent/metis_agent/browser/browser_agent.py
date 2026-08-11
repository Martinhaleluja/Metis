"""
Metis Browser Automation Subsystem using Playwright
"""
import asyncio
from typing import Optional, Dict, Any

try:
    from playwright.async_api import async_playwright, Browser, Page
    PLAYWRIGHT_AVAILABLE = True
except ImportError:
    PLAYWRIGHT_AVAILABLE = False
    Browser = Any
    Page = Any

class MetisBrowserAgent:
    def __init__(self):
        self._playwright = None
        self._browser: Optional[Browser] = None
        self._page: Optional[Page] = None

    async def initialize(self, headless: bool = False):
        if not PLAYWRIGHT_AVAILABLE:
            raise RuntimeError("Playwright package is not installed.")
        if not self._playwright:
            self._playwright = await async_playwright().start()
            self._browser = await self._playwright.chromium.launch(headless=headless)
            self._page = await self._browser.new_page()

    async def navigate(self, url: str) -> Dict[str, Any]:
        if not self._page:
            await self.initialize()
        response = await self._page.goto(url)
        title = await self._page.title()
        return {"url": self._page.url, "title": title, "status": response.status if response else 200}

    async def click(self, selector: str) -> bool:
        if not self._page:
            return False
        # Fallback hierarchy: DOM selector -> text selector
        try:
            await self._page.click(selector, timeout=5000)
            return True
        except Exception:
            try:
                await self._page.click(f"text={selector}", timeout=5000)
                return True
            except Exception:
                return False

    async def fill(self, selector: str, value: str) -> bool:
        if not self._page:
            return False
        try:
            await self._page.fill(selector, value, timeout=5000)
            return True
        except Exception:
            return False

    async def extract_content(self) -> str:
        if not self._page:
            return ""
        return await self._page.content()

    async def close(self):
        if self._browser:
            await self._browser.close()
        if self._playwright:
            await self._playwright.stop()
        self._browser = None
        self._playwright = None
        self._page = None
