# Presentation Showcase System - Setup & Integration Guide

This guide will walk you through setting up the complete Presentation Showcase system you just added to your Unity project.

---

## Step 1: Camera & Cinemachine Setup

1. **Main Camera Setup:**
   - Select your **Main Camera** in the hierarchy.
   - Click `Add Component` and add a **CinemachineBrain**.
   - Adjust the **Default Blend** to `Ease In Out` with a duration of `2` seconds (or whatever matches your desired transition time).

2. **Location Virtual Cameras:**
   - For every presentation slide GameObject you have (e.g., `iNTRO`, `Team members`, `Art Showcase`), add a **CinemachineVirtualCamera** as a child of that object (or to the object itself).
   - Position and configure the Virtual Camera for how you want that specific slide to be viewed.

---

## Step 2: Configure the Locations

For every slide/location object in your hierarchy:
1. Select the GameObject (e.g., `iNTRO`).
2. Click `Add Component` and add the **PresentationLocation** script.
3. _(Optional)_ If you have specific things that need to reset when you arrive at this slide (like a particle effect, or a character animation), you can use the **On Location Reset** event in the inspector to hook up those triggers.

---

## Step 3: Configure the Presentation Manager

1. Create a new Empty GameObject in your `PresentationScene` and name it **"PresentationManager"**.
2. Click `Add Component` and add the **PresentationManager** script.
3. In the Inspector for the Manager:
   - **Transition Delay**: Set this to match your CinemachineBrain blend duration (e.g., `2.0`). This ensures the old slide waits for the camera transition to finish before disappearing.
   - **Locations List**: Expand the list and drag all of your Location GameObjects (`iNTRO`, `Team members`, etc.) into this list **in the exact order** you want them presented.

---

## Step 4: Hook Up the UI (Canvas)

1. **Navigation Buttons (Next/Previous)**
   - Find your "Next" and "Previous" UI Buttons on your Canvas.
   - In the **Button (Script)** component, find the `On Click ()` event.
   - Click the **`+`** icon to add a new event.
   - Drag the **PresentationManager** GameObject into the empty object slot.
   - From the dropdown, select **`PresentationManager -> NextLocation`** (or `PreviousLocation`).

2. **Custom Actions (E.g. Play Video, Show Stats)**
   - If you have a custom button (like a "Play Video" button) on your Canvas, go to its `On Click ()` event.
   - Drag the **PresentationManager** GameObject into the slot.
   - Select **`PresentationManager -> TriggerCustomAction(string)`**.
   - In the text box that appears below it, type the ID of the action (e.g., `PlayVideo`).
   - _Note: Remember to attach a script implementing `IPresentationCustomAction` to the specific slide(s) that should respond to this command!_

---

## Step 5: Test the System!

1. Hit **Play** in the editor.
2. The Manager will automatically deactivate all slides except the first one in your list.
3. Click your UI **Next** button. 
4. The next slide will activate, the Cinemachine camera will beautifully blend over to the new slide, and after the 2-second delay, the previous slide will cleanly shut down to save performance.
