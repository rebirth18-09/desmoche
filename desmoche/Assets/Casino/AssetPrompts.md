# Casino bitmap assets

Generated with the built-in image_gen tool; no CLI/API fallback was used.
Original generated images remain in the Codex generated_images directory. The copies in this folder are the production resources consumed by the Godot project.

## CasinoTable.png

Use case: precise-object-edit.
Asset type: production background texture for a Godot card game, clean game board with no gameplay objects.
Edit target: the supplied reference image. Remove EVERY playing card, deck, discard placeholder, player portrait, name, player panel, avatar circle, all text, numbers, labels, decorations resembling UI, and counters. Reconstruct the materials underneath completely.
Keep the wide landscape composition and luxurious materials as close to the reference as possible: warm realistic dark walnut wood outside, an enormous horizontal oval/rounded poker table, deep emerald green subtly mottled felt, thick padded black leather rim with delicate thin double gold trim and soft highlights, and a separate broad shallow black leather hand tray below with a thin gold border. No seats or people.
Composition: ultra wide about 2.585:1 (reference 2016 by 780). Board bounds x=1%, y=14.5%, width=98%, height=64% of canvas; upper 14% remains uncluttered dark walnut for later player overlays. Bottom separate leather tray bounds x=7%, y=81.5%, width=86%, height=17.5%. Green playing surface must be completely blank from left to right. Preserve reference perspective, almost top-down, slightly frontal.
Materials: photorealistic fine felt fibers, detailed soft leather grain and stitching, warm polished wood grain. Smooth natural lighting and vignette, no artificial horizontal bands or stripes in the felt, no harsh contrast. Professional game-art asset, crisp at high resolution. Prefer width 3840 pixels and proportional height approximately 1488 if available.
Constraints: absolutely no cards, no UI panels, no writing or symbols of any sort, no avatars, no chips, no tools, no watermark. Keep only wood, oval felt table, padded leather rims, gold trim, empty bottom tray.

## Portraits.png

Use case: photorealistic-natural.
Asset type: production portrait texture atlas for a casino card game. A perfectly regular 2 by 2 atlas of FOUR different photographic head-and-shoulders portraits. Canvas square preferably 2048 by 2048. Exact four equal quadrants with no gutters or gaps, each subject centered in its own square. No circles or frames baked into image; UI will clip each portrait to a circle later.
Top-left: adult woman about 30, natural warm medium skin, long chestnut brown hair, understated makeup, dark neutral blouse, friendly composed expression.
Top-right: adult man about 35, short dark hair and neatly trimmed dark beard, warm medium skin, dark neutral shirt, relaxed smile.
Bottom-left: adult man about 30, warm medium skin, wearing a plain dark grey baseball cap with no logos, neat stubble, casual dark top, pleasant expression.
Bottom-right: adult clean-shaven man about 30, short dark brown hair, warm medium skin, dark neutral shirt, slight welcoming smile.
All four have comparable natural photographic style, camera eye level, medium close-up, shoulders visible but face large, face/head contained within central 65% of each quadrant so circular crop preserves entire face. Soft cinematic warm casino lighting, realistic skin detail and subtle rim light; softly blurred very dark teal/brown backgrounds. Equal face scale across all four quadrants.
Constraints: four separate people only, no lettering, no logos, no poker cards, no labels, no names, no numbers, no borders, no visible tile dividers, no watermark. The division into four portraits is exact at x=50% and y=50%; no subject crossing those boundaries.

## Courts.png

Use case: historical-scene.
Asset type: three-cell horizontal texture atlas of English playing-card COURT ILLUSTRATIONS, intended for the center of real playing cards. Produce ONE wide canvas split into EXACTLY THREE equal-width cells with no gutters, in left-to-right order JACK, QUEEN, KING. Prefer 2304 wide by 1536 tall, each cell 768 by 1536 or similar. Each cell contains a fully visible traditional double-headed court figure filling 95% of its cell height and 90% width, almost no outer padding.
Art style: refined traditional English Anglo-American playing-card engraving, richly colored red, navy blue, golden yellow, black line details; authentic intricate geometric garments and crowns, thin ink contours, charming classic printed card faces. Flat print-ready illustration, high detail, crisp.
Composition per cell: a proper two-way mirrored double-headed figure, upper bust upright and matching lower bust rotated 180 degrees, perfectly joined at horizontal center, no whole playing card frame around them. JACK: youthful moustached royal courtier with feathered cap and sceptre, no crown. QUEEN: elegant royal woman wearing a jeweled crown and ornate robes, holding a stylized flower. KING: mature bearded crowned king wearing rich robes and holding sword. Recognizable distinct faces and poses. The mirrored lower halves must be complete and avoid missing/cutoff head/hat/crown. Pale warm IVORY background identical across all cells, RGB approximately 250/247/231. The figure art may have a fine dark rectangular ornamental internal outline at the edge of its costume, but NO outer card frame or rank areas.
Constraints: absolutely no letters J Q K or any other letters, absolutely no numbers, NO HEART/DIAMOND/SPADE/CLUB SUIT ICONS anywhere, no corner indices, no title, no watermark, no words, no photographic realism, no casino background, no frame separating cells. The cells divide at exactly one third and two thirds of canvas width; every figure remains completely in its own cell. Color is part of illustration only; UI will add rank and suit from game data separately.

## Visual inspection

All outputs were inspected with view_image. The table is free of cards, text, avatars and UI panels. Portraits occupy four equal quadrants and share natural warm lighting. Courts contain three distinct complete two-way mirrored figures without baked rank/suit labels. Actual output sizes are listed in AssetsManifest.json; the generator chose smaller sizes than requested, so these should not be described as 4K resources.
